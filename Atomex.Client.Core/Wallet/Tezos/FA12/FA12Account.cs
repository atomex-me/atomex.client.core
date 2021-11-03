using System;
using System.Collections.Generic;
using System.Linq;
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
    public class Fa12Account : TezosTokenAccount
    {
        private Fa12Config Fa12Config => Currencies.Get<Fa12Config>(Currency);

        public Fa12Account(
            string currency,
            string tokenContract,
            decimal tokenId,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository,
            TezosAccount tezosAccount)
            : base(currency,
                  "FA12",
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
            decimal amount,
            decimal fee,
            bool useDefaultFee = true,
            CancellationToken cancellationToken = default)
        {
            if (from == to)
                return new Error(
                    code: Errors.SendingAndReceivingAddressesAreSame,
                    description: "Sending and receiving addresses are the same.");

            var fa12Config = Fa12Config;
            var xtzConfig = XtzConfig;

            var addressFeeUsage = await CalculateFundsUsageAsync(
                    from: from,
                    amount: amount,
                    fee: fee,
                    feeUsagePolicy: useDefaultFee
                        ? FeeUsagePolicy.EstimatedFee
                        : FeeUsagePolicy.FeePerTransaction,
                    transactionType: BlockchainTransactionType.Output,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (addressFeeUsage == null)
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: "Insufficient funds");

            var digitsMultiplier = fa12Config.DigitsMultiplier != 0
                ? fa12Config.DigitsMultiplier
                : (decimal)Math.Pow(10, addressFeeUsage.WalletAddress.TokenBalance.Decimals);

            var addressAmountInDigits = addressFeeUsage.UsedAmount.ToTokenDigits(digitsMultiplier);

            Log.Debug("Send {@amount} tokens from address {@address} with available balance {@balance}",
                addressAmountInDigits,
                addressFeeUsage.WalletAddress.Address,
                addressFeeUsage.WalletAddress.AvailableBalance());

            var storageLimit = Math.Max(fa12Config.TransferStorageLimit - fa12Config.ActivationStorage, 0); // without activation storage fee
               
            var tx = new TezosTransaction
            {
                Currency      = xtzConfig.Name,
                CreationTime  = DateTime.UtcNow,
                From          = addressFeeUsage.WalletAddress.Address,
                To            = _tokenContract,
                Fee           = addressFeeUsage.UsedFee.ToMicroTez(),
                GasLimit      = fa12Config.TransferGasLimit,
                StorageLimit  = storageLimit,
                Params        = TransferParams(addressFeeUsage.WalletAddress.Address, to, Math.Round(addressAmountInDigits, 0)),
                Type          = BlockchainTransactionType.Output | BlockchainTransactionType.TokenCall,

                UseRun              = useDefaultFee,
                UseSafeStorageLimit = true,
                UseOfflineCounter   = true
            };

            using var addressLock = await _tezosAccount.AddressLocker
                .GetLockAsync(addressFeeUsage.WalletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            using var securePublicKey = Wallet.GetPublicKey(
                currency: xtzConfig,
                keyIndex: addressFeeUsage.WalletAddress.KeyIndex,
                keyType: addressFeeUsage.WalletAddress.KeyType);

            // fill operation
            var (fillResult, isRunSuccess) = await tx
                .FillOperationsAsync(
                    securePublicKey: securePublicKey,
                    tezosConfig: xtzConfig,
                    headOffset: TezosConfig.HeadOffset,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var signResult = await Wallet
                .SignAsync(tx, addressFeeUsage.WalletAddress, xtzConfig, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
                return new Error(
                    code: Errors.TransactionSigningError,
                    description: "Transaction signing error");

            var broadcastResult = await XtzConfig.BlockchainApi
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

            return null;
        }

        public async Task<decimal?> EstimateFeeAsync(
            string from,
            string to,
            decimal amount,
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default)
        {
            if (from == to || string.IsNullOrEmpty(from))
                return null;

            var addressFeeUsage = await CalculateFundsUsageAsync(
                    from: from,
                    amount: amount,
                    fee: 0,
                    feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                    transactionType: type,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (addressFeeUsage == null)
                return null; // insufficient funds

            return addressFeeUsage.UsedFee;
        }

        public override async Task<(decimal fee, bool isEnougth)> EstimateTransferFeeAsync(
            string from,
            CancellationToken cancellationToken = default)
        {
            var xtzAddress = await _tezosAccount
                .GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var txFeeInTez = await FeeByType(
                    type: BlockchainTransactionType.Output,
                    from: xtzAddress?.Address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInTez = StorageFeeByTypeAsync(
                type: BlockchainTransactionType.Output);

            var requiredFeeInTez = txFeeInTez + storageFeeInTez + XtzConfig.MicroTezReserve.ToTez();

            var availableBalanceInTez = xtzAddress != null
                ? xtzAddress.AvailableBalance()
                : 0m;

            return (
                fee: requiredFeeInTez,
                isEnougth: availableBalanceInTez >= requiredFeeInTez);
        }

        public async Task<(decimal, decimal, decimal)> EstimateMaxAmountToSendAsync(
            string from,
            string to,
            BlockchainTransactionType type,
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            if (from == to || string.IsNullOrEmpty(from))
                return (0m, 0m, 0m); // invalid addresses

            var xtz = XtzConfig;

            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return (0m, 0m, 0m); // invalid address

            var reserveFee = ReserveFee();

            var xtzAddress = await DataRepository
                .GetWalletAddressAsync(xtz.Name, fromAddress.Address)
                .ConfigureAwait(false);

            if (xtzAddress == null)
                return (0m, 0m, 0m); // insufficient funds

            var feeInTez = await FeeByType(
                    type: type,
                    from: fromAddress.Address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInTez = StorageFeeByTypeAsync(type);

            var restBalanceInTez = xtzAddress.AvailableBalance() -
                feeInTez -
                storageFeeInTez -
                (reserve ? reserveFee : 0) -
                xtz.MicroTezReserve.ToTez();

            if (restBalanceInTez < 0)
                return (0m, 0m, 0m); // insufficient funds

            if (fromAddress.AvailableBalance() <= 0)
                return (0m, 0m, 0m); // insufficient funds

            return (fromAddress.AvailableBalance(), feeInTez, reserveFee);
        }

        private async Task<decimal> FeeByType(
            BlockchainTransactionType type,
            string from,
            CancellationToken cancellationToken = default)
        {
            var fa12 = Fa12Config;

            var isRevealed = from != null && await _tezosAccount
                .IsRevealedSourceAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var revealFeeInTez = !isRevealed
                ? fa12.RevealFee.ToTez()
                : 0;

            if (type.HasFlag(BlockchainTransactionType.TokenApprove))
                return fa12.ApproveFee.ToTez();

            if (type.HasFlag(BlockchainTransactionType.SwapPayment))
                return fa12.ApproveFee.ToTez() * 2 + fa12.InitiateFee.ToTez() + revealFeeInTez;

            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return fa12.RefundFee.ToTez() + revealFeeInTez;

            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return fa12.RedeemFee.ToTez() + revealFeeInTez;

            return fa12.TransferFee.ToTez() + revealFeeInTez;
        }

        private decimal ReserveFee()
        {
            var xtz = XtzConfig;
            var fa12 = Fa12Config;

            return new[]
            {
                (fa12.RedeemFee + Math.Max((fa12.RedeemStorageLimit - fa12.ActivationStorage) * fa12.StorageFeeMultiplier, 0)).ToTez(),
                (fa12.RefundFee + Math.Max((fa12.RefundStorageLimit - fa12.ActivationStorage) * fa12.StorageFeeMultiplier, 0)).ToTez(),
                (xtz.RedeemFee + Math.Max((xtz.RedeemStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0)).ToTez(),
                (xtz.RefundFee + Math.Max((xtz.RefundStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0)).ToTez()

            }.Max() + fa12.RevealFee.ToTez() + XtzConfig.MicroTezReserve.ToTez();
        }

        private decimal StorageFeeByTypeAsync(
            BlockchainTransactionType type)
        {
            var fa12 = Fa12Config;

            if (type.HasFlag(BlockchainTransactionType.TokenApprove))
                return fa12.ApproveStorageLimit.ToTez();

            if (type.HasFlag(BlockchainTransactionType.SwapPayment))
                return ((fa12.ApproveStorageLimit * 2 + fa12.InitiateStorageLimit) * fa12.StorageFeeMultiplier).ToTez();

            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return ((fa12.RefundStorageLimit - fa12.ActivationStorage) * fa12.StorageFeeMultiplier).ToTez();

            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return ((fa12.RedeemStorageLimit - fa12.ActivationStorage) * fa12.StorageFeeMultiplier).ToTez();

            return ((fa12.TransferStorageLimit - fa12.ActivationStorage) * fa12.StorageFeeMultiplier).ToTez();
        }

        #endregion Common

        #region Addresses

        public Task<WalletAddress> GetRedeemAddressAsync( // todo: match it with xtz balances
            CancellationToken cancellationToken = default)
        {
            return GetFreeExternalAddressAsync(cancellationToken);
        }

        private async Task<SelectedWalletAddress> CalculateFundsUsageAsync(
            string from,
            decimal amount,
            decimal fee,
            FeeUsagePolicy feeUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default)
        {
            var xtz = XtzConfig;

            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return null; // invalid address

            var xtzAddress = await DataRepository
                .GetWalletAddressAsync(xtz.Name, fromAddress.Address)
                .ConfigureAwait(false);

            var availableBalanceInTez = xtzAddress?.AvailableBalance() ?? 0m;

            var txFeeInTez = feeUsagePolicy == FeeUsagePolicy.EstimatedFee
                ? await FeeByType(
                        type: transactionType,
                        from: fromAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
                : fee;

            var storageFeeInTez = StorageFeeByTypeAsync(transactionType);

            var restBalanceInTez = availableBalanceInTez -
                txFeeInTez -
                storageFeeInTez -
                xtz.MicroTezReserve.ToTez();

            if (restBalanceInTez < 0)
            {
                Log.Debug("Unsufficient XTZ ammount for FA12 token processing on address {@address} with available balance {@balance} and needed amount {@amount}",
                    fromAddress.Address,
                    availableBalanceInTez,
                    txFeeInTez + storageFeeInTez + xtz.MicroTezReserve.ToTez());

                return null;
            }

            var restBalanceInTokens = fromAddress.AvailableBalance() - amount;

            if (restBalanceInTokens < 0) // todo: log?
                return null;

            return new SelectedWalletAddress
            {
                WalletAddress  = fromAddress,
                UsedAmount     = amount,
                UsedFee        = txFeeInTez,
                UsedStorageFee = storageFeeInTez
            };
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentTokenAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return DataRepository
                .GetUnspentTezosTokenAddressesAsync(TokenType, _tokenContract, _tokenId);
        }

        #endregion Addresses

        #region Helpers

        private JObject TransferParams(string from, string to, decimal amount)
        {
            return JObject.FromObject(new
            {
                entrypoint = "transfer",
                value = new
                {
                    prim = "Pair",
                    args = new object[]
                    {
                        new
                        {
                            @string = from
                        },
                        new
                        {
                            prim = "Pair",
                            args = new object[]
                            {
                                new
                                {
                                    @string = to
                                },
                                new
                                {
                                    @int = amount.ToString()
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