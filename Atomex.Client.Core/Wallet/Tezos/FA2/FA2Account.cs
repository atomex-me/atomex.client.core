using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.TezosTokens;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;

namespace Atomex.Wallet.Tezos
{
    public class Fa2Account : CurrencyAccount
    {
        private readonly TezosAccount _tezosAccount;
        private Fa2Config Fa2Config => Currencies.Get<Fa2Config>(Currency);
        private TezosConfig XtzConfig => Currencies.Get<TezosConfig>(TezosConfig.Xtz);

        public Fa2Account(
            string currency,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository,
            TezosAccount tezosAccount)
                : base(currency, currencies, wallet, dataRepository)
        {
            _tezosAccount = tezosAccount;
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
            var fa2 = Fa2Config;
            var xtz = XtzConfig;

            var fa2token = TezosConfig.UniqueTokenId(tokenContract, tokenId, "FA2");

            var fromAddress = await DataRepository
                .GetTezosTokenAddressAsync(fa2token, from)
                .ConfigureAwait(false);

            if (fromAddress.AvailableBalance() < amount)
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: $"Insufficient tokens. " +
                        $"Available: {fromAddress.AvailableBalance()}. " +
                        $"Required: {amount}.");

            var xtzAddress = await DataRepository
                .GetWalletAddressAsync(xtz.Name, from)
                .ConfigureAwait(false);

            var isRevealed = await _tezosAccount
                .IsRevealedSourceAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInMtz = (fa2.TransferStorageLimit - fa2.ActivationStorage) * fa2.StorageFeeMultiplier;

            var feeInMtz = useDefaultFee
                ? fa2.TransferFee + (isRevealed ? 0 : fa2.RevealFee) + storageFeeInMtz
                : fee;

            var availableBalanceInTz = xtzAddress.AvailableBalance().ToMicroTez() - feeInMtz - xtz.MicroTezReserve;

            if (availableBalanceInTz < 0)
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: $"Insufficient funds to pay fee for address {from}. " +
                        $"Available: {xtzAddress.AvailableBalance()}. " +
                        $"Required: {feeInMtz + xtz.MicroTezReserve}");

            Log.Debug("Send {@amount} tokens from address {@address} with available balance {@balance}",
                amount,
                from,
                fromAddress.AvailableBalance());

            var storageLimit = Math.Max(fa2.TransferStorageLimit - fa2.ActivationStorage, 0); // without activation storage fee

            var tx = new TezosTransaction
            {
                Currency     = xtz,
                CreationTime = DateTime.UtcNow,
                From         = from,
                To           = tokenContract,
                Fee          = feeInMtz,
                GasLimit     = fa2.TransferGasLimit,
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
                    .GetPublicKey(xtz, fromAddress.KeyIndex);

                // fill operation
                var fillResult = await tx
                    .FillOperationsAsync(
                        securePublicKey: securePublicKey,
                        headOffset: TezosConfig.HeadOffset,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var signResult = await Wallet
                    .SignAsync(tx, fromAddress, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                    return new Error(
                        code: Errors.TransactionSigningError,
                        description: "Transaction signing error");

                var broadcastResult = await xtz.BlockchainApi
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

        protected override Task<bool> ResolveTransactionTypeAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        #endregion Common

        #region Balances

        public override Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var scanner = new TezosTokensScanner(_tezosAccount);

                await scanner
                    .ScanContractAsync(TezosConfig.ExtractContract(Currency), cancellationToken)
                    .ConfigureAwait(false);

                LoadBalances();

                RaiseBalanceUpdated(new CurrencyEventArgs(Currency));

            }, cancellationToken);
        }

        public override Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var scanner = new TezosTokensScanner(_tezosAccount);

                await scanner
                    .ScanContractAsync(address, TezosConfig.ExtractContract(Currency), cancellationToken)
                    .ConfigureAwait(false);

                LoadBalances();

                RaiseBalanceUpdated(new CurrencyEventArgs(Currency));

            }, cancellationToken);
        }

        #endregion Balances

        #region Addresses

        public override async Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            // 1. try to find address with tokens
            var addresses = (await DataRepository
                .GetUnspentTezosTokenAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (addresses.Any())
                return addresses.MaxBy(a => a.AvailableBalance());

            // 2. try to find xtz address with max balance
            var unspentXtzAddresses = await DataRepository
                .GetUnspentAddressesAsync(TezosConfig.Xtz)
                .ConfigureAwait(false);

            if (unspentXtzAddresses.Any())
            {
                var xtzAddress = unspentXtzAddresses.MaxBy(a => a.AvailableBalance());

                var fa2Address = await DataRepository
                    .GetTezosTokenAddressAsync(Currency, xtzAddress.Address)
                    .ConfigureAwait(false);

                if (fa2Address != null)
                    return fa2Address;

                fa2Address = Wallet.GetAddress(
                    Fa2Config,
                    xtzAddress.KeyIndex.Chain,
                    xtzAddress.KeyIndex.Index);

                await DataRepository
                    .TryInsertTezosTokenAddressAsync(fa2Address)
                    .ConfigureAwait(false);

                return fa2Address;
            }

            // 3. use free xtz address
            var freeXtzAddress = await _tezosAccount
                .GetFreeExternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            var freeFa2Address = await DataRepository
                .GetTezosTokenAddressAsync(Currency, freeXtzAddress.Address)
                .ConfigureAwait(false);

            if (freeFa2Address != null)
                return freeFa2Address;

            freeFa2Address = Wallet.GetAddress(
                Fa2Config,
                freeXtzAddress.KeyIndex.Chain,
                freeXtzAddress.KeyIndex.Index);

            await DataRepository
                .TryInsertTezosTokenAddressAsync(freeFa2Address)
                .ConfigureAwait(false);

            return freeFa2Address;
        }

        #endregion Addresses

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