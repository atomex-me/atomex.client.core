using System;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Core;
using Atomex.TezosTokens;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet.Tezos
{
    public class Fa2Account : TezosTokenAccount
    {
        private Fa2Config Fa2Config => Currencies.Get<Fa2Config>(Currency);

        public Fa2Account(
            string currency,
            string tokenContract,
            decimal tokenId,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository,
            TezosAccount tezosAccount)
            : base(currency,
                  tokenContract,
                  tokenId,
                  currencies,
                  wallet,
                  dataRepository,
                  tezosAccount)
        {
        }

        #region Common

        public async Task<Error> SendAsync(
            string from,
            string to,
            int amount,
            string tokenContract,
            int tokenId,
            int fee,
            bool useDefaultFee = true,
            CancellationToken cancellationToken = default)
        {
            var fa2Config = Fa2Config;
            var xtzConfig = XtzConfig;

            var fromAddress = await DataRepository
                .GetTezosTokenAddressAsync(Currency, _tokenContract, _tokenId, from)
                .ConfigureAwait(false);

            if (fromAddress.AvailableBalance() < amount)
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: $"Insufficient tokens. " +
                        $"Available: {fromAddress.AvailableBalance()}. " +
                        $"Required: {amount}.");

            var xtzAddress = await DataRepository
                .GetWalletAddressAsync(xtzConfig.Name, from)
                .ConfigureAwait(false);

            var isRevealed = await _tezosAccount
                .IsRevealedSourceAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInMtz = (fa2Config.TransferStorageLimit - fa2Config.ActivationStorage) * fa2Config.StorageFeeMultiplier;

            var feeInMtz = useDefaultFee
                ? fa2Config.TransferFee + (isRevealed ? 0 : fa2Config.RevealFee) + storageFeeInMtz
                : fee;

            var availableBalanceInTz = xtzAddress.AvailableBalance().ToMicroTez() - feeInMtz - xtzConfig.MicroTezReserve;

            if (availableBalanceInTz < 0)
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: $"Insufficient funds to pay fee for address {from}. " +
                        $"Available: {xtzAddress.AvailableBalance()}. " +
                        $"Required: {feeInMtz + xtzConfig.MicroTezReserve}");

            Log.Debug("Send {@amount} tokens from address {@address} with available balance {@balance}",
                amount,
                from,
                fromAddress.AvailableBalance());

            var storageLimit = Math.Max(fa2Config.TransferStorageLimit - fa2Config.ActivationStorage, 0); // without activation storage fee

            var tx = new TezosTransaction
            {
                Currency     = xtzConfig.Name,
                CreationTime = DateTime.UtcNow,
                From         = from,
                To           = tokenContract,
                Fee          = feeInMtz,
                GasLimit     = fa2Config.TransferGasLimit,
                StorageLimit = storageLimit,
                Params       = TransferParams(tokenId, from, to, amount),
                Type         = BlockchainTransactionType.Output | BlockchainTransactionType.TokenCall,

                UseRun              = useDefaultFee,
                UseSafeStorageLimit = true,
                UseOfflineCounter   = true
            };

            try
            {
                await _tezosAccount.AddressLocker
                    .LockAsync(from, cancellationToken)
                    .ConfigureAwait(false);

                using var securePublicKey = Wallet
                    .GetPublicKey(xtzConfig, fromAddress.KeyIndex);

                // fill operation
                var fillResult = await tx
                    .FillOperationsAsync(
                        securePublicKey: securePublicKey,
                        tezosConfig: xtzConfig,
                        headOffset: TezosConfig.HeadOffset,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var signResult = await Wallet
                    .SignAsync(tx, fromAddress, xtzConfig, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                    return new Error(
                        code: Errors.TransactionSigningError,
                        description: "Transaction signing error");

                var broadcastResult = await xtzConfig.BlockchainApi
                    .TryBroadcastAsync(tx, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (broadcastResult.HasError)
                    return broadcastResult.Error;

                var txId = broadcastResult.Value;

                if (txId == null)
                    return new Error(
                        code: Errors.TransactionBroadcastError,
                        description: "Transaction Id is null");

                Log.Debug("Transaction successfully sent with txId: {@id}", txId);

                await _tezosAccount
                    .UpsertTransactionAsync(
                        tx: tx,
                        updateBalance: false,
                        notifyIfUnconfirmed: true,
                        notifyIfBalanceUpdated: false,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _tezosAccount.AddressLocker
                    .Unlock(from);
            }

            return null;
        }

        public override async Task<(decimal fee, bool isEnougth)> EstimateTransferFeeAsync(
            string from,
            CancellationToken cancellationToken = default)
        {
            var fa2Config = Fa2Config;
            var xtzConfig = XtzConfig;

            var xtzAddress = await _tezosAccount
                .GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var isRevealed = xtzAddress?.Address != null && await _tezosAccount
                .IsRevealedSourceAsync(xtzAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInMtz = (fa2Config.TransferStorageLimit - fa2Config.ActivationStorage) * fa2Config.StorageFeeMultiplier;

            var feeInMtz = fa2Config.TransferFee + (isRevealed ? 0 : fa2Config.RevealFee) + storageFeeInMtz + xtzConfig.MicroTezReserve;

            var availableBalanceInTez = xtzAddress != null
                ? xtzAddress.AvailableBalance()
                : 0m;

            return (
                fee: feeInMtz.ToTez(),
                isEnougth: availableBalanceInTez >= feeInMtz.ToTez());
        }

        #endregion Common

        #region Helpers

        private JObject TransferParams(
            int tokenId,
            string from,
            string to,
            int amount)
        {
            return JObject.FromObject(new
            {
                entrypoint = "transfer",
                value = new object[]
                {
                    new
                    {
                        prim = "Pair",
                        args = new object[]
                        {
                            new
                            {
                                @string = from
                            },
                            new object[]
                            {
                                new
                                {
                                    prim = "Pair",
                                    args = new object[]
                                    {
                                        new
                                        {
                                            @string = to,
                                        },
                                        new
                                        {
                                            prim = "Pair",
                                            args = new object[]
                                            {
                                                new
                                                {
                                                    @int = tokenId.ToString()
                                                },
                                                new
                                                {
                                                    @int = amount.ToString()
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            });
        }

        #endregion Helpers
    }
}