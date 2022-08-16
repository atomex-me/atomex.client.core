using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;

namespace Atomex.Wallet.Tezos
{
    public class TezosAccount : CurrencyAccount, IEstimatable, IHasTokens
    {
        private readonly TezosRevealChecker _tezosRevealChecker;
        private readonly TezosAllocationChecker _tezosAllocationChecker;

        public readonly ResourceLocker<string> AddressLocker;

        public TezosAccount(
            ICurrencies currencies,
            IHdWallet wallet,
            ILocalStorage dataRepository)
                : base(TezosConfig.Xtz, currencies, wallet, dataRepository)
        {
            var xtz = Config;

            _tezosRevealChecker = new TezosRevealChecker(xtz);
            _tezosAllocationChecker = new TezosAllocationChecker(xtz);

            AddressLocker = new ResourceLocker<string>();
        }

        #region Common

        public TezosConfig Config => Currencies.Get<TezosConfig>(Currency);

        public async Task<(string txId, Error error)> SendAsync(
            string from,
            string to,
            decimal amount,
            decimal fee,
            bool useDefaultFee = true,
            CancellationToken cancellationToken = default)
        {
            //if (from == to)
            //    return new Error(
            //        code: Errors.SendingAndReceivingAddressesAreSame,
            //        description: "Sending and receiving addresses are the same.");

            var xtzConfig = Config;

            var addressFeeUsage = await CalculateFundsUsageAsync(
                    from: from,
                    to: to,
                    amount: amount,
                    fee: fee,
                    feeUsagePolicy: useDefaultFee
                        ? FeeUsagePolicy.EstimatedFee
                        : FeeUsagePolicy.FeePerTransaction,
                    transactionType: BlockchainTransactionType.Output,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (addressFeeUsage == null)
                return (
                    txId: null,
                    error: new Error(
                        code: Errors.InsufficientFunds,
                        description: "Insufficient funds"));

            var isActive = await IsAllocatedDestinationAsync(to, cancellationToken)
                .ConfigureAwait(false);

            // todo: min fee control
            var addressAmountMtz = addressFeeUsage.UsedAmount.ToMicroTez();

            Log.Debug("Send {@amount} XTZ from address {@address} with available balance {@balance}",
                addressFeeUsage.UsedAmount,
                addressFeeUsage.WalletAddress.Address,
                addressFeeUsage.WalletAddress.AvailableBalance());

            var storageLimit = isActive
                ? Math.Max(xtzConfig.StorageLimit - xtzConfig.ActivationStorage, 0) // without activation storage fee
                : xtzConfig.StorageLimit;

            var tx = new TezosTransaction
            {
                Currency      = xtzConfig.Name,
                CreationTime  = DateTime.UtcNow,
                From          = addressFeeUsage.WalletAddress.Address,
                To            = to,
                Amount        = Math.Round(addressAmountMtz, 0),
                Fee           = addressFeeUsage.UsedFee.ToMicroTez(),
                GasLimit      = xtzConfig.GasLimit,
                StorageLimit  = storageLimit,
                Type          = BlockchainTransactionType.Output,

                UseRun              = false, //useDefaultFee,
                UseSafeStorageLimit = false,
                UseOfflineCounter   = true
            };

            using var addressLock = await AddressLocker
                .GetLockAsync(addressFeeUsage.WalletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            // temporary fix: check operation sequence
            await TezosOperationsSequencer
                .WaitAsync(addressFeeUsage.WalletAddress.Address, this, cancellationToken)
                .ConfigureAwait(false);

            using var securePublicKey = Wallet.GetPublicKey(
                currency: xtzConfig,
                keyIndex: addressFeeUsage.WalletAddress.KeyIndex,
                keyType: addressFeeUsage.WalletAddress.KeyType);

            // fill operation
            var (fillResult, isRunSuccess, hasReveal) = await tx
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
                return (
                    txId: null,
                    error: new Error(
                        code: Errors.TransactionSigningError,
                        description: "Transaction signing error"));

            var broadcastResult = await xtzConfig.BlockchainApi
                .TryBroadcastAsync(tx, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
                return (txId: null, error: broadcastResult.Error);

            var txId = broadcastResult.Value;

            if (txId == null)
                return (
                    txId: null,
                    error: new Error(
                        code: Errors.TransactionBroadcastError,
                        description: "Transaction Id is null"));

            Log.Debug("Transaction successfully sent with txId: {@id}", txId);

            var _ = await LocalStorage
                .UpsertTransactionAsync(
                    tx: tx,
                    notifyIfNewOrChanged: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return (txId, error: null);
        }

        public async Task<decimal> EstimateFeeAsync(
            string from,
            string to,
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default)
        {
            var txFeeInTez = await FeeByType(
                    type: type,
                    from: from,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInTez = await StorageFeeByTypeAsync(
                    type: type,
                    to: to,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return txFeeInTez + storageFeeInTez;
        }

        public async Task<decimal?> EstimateSwapPaymentFeeAsync(
            IFromSource from,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            var fromAddress = (from as FromAddress)?.Address;

            return await EstimateFeeAsync(
                    from: fromAddress,
                    to: null,
                    type: BlockchainTransactionType.SwapPayment,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<MaxAmountEstimation> EstimateMaxAmountToSendAsync(
            string from,
            string to,
            BlockchainTransactionType type,
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(from))
                return new MaxAmountEstimation {
                    Error = new Error(Errors.FromAddressIsNullOrEmpty, Resources.FromAddressIsNullOrEmpty)
                };

            //if (from == to)
            //    return new MaxAmountEstimation {
            //        Error = new Error(Errors.SendingAndReceivingAddressesAreSame, "Sending and receiving addresses are same")
            //    };

            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return new MaxAmountEstimation {
                    Error = new Error(Errors.AddressNotFound, Resources.AddressNotFoundInLocalDb)
                };

            var reserveFee = ReserveFee();

            var feeInTez = await FeeByType(
                    type: type,
                    from: fromAddress.Address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInTez = await StorageFeeByTypeAsync(
                    type: type,
                    to: to,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var requiredFeeInTez = feeInTez +
                storageFeeInTez +
                (reserve ? reserveFee : 0);

            var requiredInTez = requiredFeeInTez +
                Config.MicroTezReserve.ToTez();

            var restAmountInTez = fromAddress.AvailableBalance() - requiredInTez;

            if (restAmountInTez < 0)
                return new MaxAmountEstimation {
                    Amount   = restAmountInTez,
                    Fee      = requiredFeeInTez,
                    Reserved = reserveFee,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        description: Resources.InsufficientFundsToCoverFees,
                        details: string.Format(
                            Resources.InsufficientFundsToCoverFeesDetails,
                            requiredInTez,
                            Currency,
                            fromAddress.AvailableBalance()))
                };

            return new MaxAmountEstimation
            {
                Amount   = restAmountInTez,
                Fee      = requiredFeeInTez,
                Reserved = reserveFee
            };
        }

        public Task<MaxAmountEstimation> EstimateMaxSwapPaymentAmountAsync(
            IFromSource from,
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            var fromAddress = (from as FromAddress)?.Address;

            return EstimateMaxAmountToSendAsync(
                from: fromAddress,
                to: null,
                type: BlockchainTransactionType.SwapPayment,
                reserve: reserve,
                cancellationToken: cancellationToken);
        }

        //protected override async Task<bool> ResolveTransactionTypeAsync(
        //    IBlockchainTransaction tx,
        //    CancellationToken cancellationToken = default)
        //{
        //    var xtz = Config;

        //    if (tx is not TezosTransaction xtzTx)
        //        throw new ArgumentException("Invalid tx type", nameof(tx));

        //    var oldTx = !xtzTx.IsInternal
        //        ? await DataRepository
        //            .GetTransactionByIdAsync<TezosTransaction>(Currency, tx.Id)
        //            .ConfigureAwait(false)
        //        : null;

        //    if (oldTx != null && oldTx.IsConfirmed)
        //        return false;

        //    var isFromSelf = await IsSelfAddressAsync(
        //            address: xtzTx.From,
        //            cancellationToken: cancellationToken)
        //        .ConfigureAwait(false);

        //    if (isFromSelf)
        //    { 
        //        xtzTx.Type |= BlockchainTransactionType.Output;

        //        var isToSwapContract = xtzTx.To == xtz.SwapContractAddress;

        //        if (isToSwapContract)
        //        {
        //            // todo: recognize swap payment/refund/redeem
        //        }
        //        else if (xtzTx.Amount == 0)
        //        {
        //            xtzTx = ResolveFa12TransactionType(xtzTx);
        //        }
        //    }

        //    var isToSelf = await IsSelfAddressAsync(
        //            address: xtzTx.To,
        //            cancellationToken: cancellationToken)
        //        .ConfigureAwait(false);

        //    if (isToSelf)
        //        xtzTx.Type |= BlockchainTransactionType.Input;

        //    if (oldTx != null)
        //        xtzTx.Type |= oldTx.Type;

        //    // todo: recognize swap payment/refund/redeem

        //    xtzTx.InternalTxs?
        //        .ForEach(async t => await ResolveTransactionTypeAsync(t, cancellationToken)
        //        .ConfigureAwait(false));

        //    ResolveTezosTxAlias(xtzTx);

        //    return true;
        //}

        //public static void ResolveTezosTxAlias(TezosTransaction tx)
        //{
        //    var ALIAS_DELIMETER = '/';

        //    if (string.IsNullOrEmpty(tx.Alias) || tx.Alias.IndexOf(ALIAS_DELIMETER) == -1)
        //        return;

        //    if (tx.Type.HasFlag(BlockchainTransactionType.Input))
        //    {
        //        tx.Alias = tx.Alias.Split(ALIAS_DELIMETER)[0];
        //    }
        //    else if (tx.Type.HasFlag(BlockchainTransactionType.Output))
        //    {
        //        tx.Alias = tx.Alias.Split(ALIAS_DELIMETER)[1];
        //    }
        //}

        //private TezosTransaction ResolveFa12TransactionType(TezosTransaction tx)
        //{
        //    if (tx.Params["entrypoint"].ToString().Equals("initiate")
        //        || tx.Params["entrypoint"].ToString().Equals("redeem")
        //        || tx.Params["entrypoint"].ToString().Equals("refund"))
        //        tx.Type |= BlockchainTransactionType.SwapCall;
        //    else if (tx.Params["entrypoint"].ToString().Equals("transfer")
        //        || tx.Params["entrypoint"].ToString().Equals("approve"))
        //        tx.Type |= BlockchainTransactionType.TokenCall;

        //    return tx;
        //}

        private async Task<decimal> FeeByType(
            BlockchainTransactionType type,
            string from,
            CancellationToken cancellationToken = default)
        {
            var xtz = Config;

            var isRevealed = await IsRevealedSourceAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var revealFeeInTez = !isRevealed
                ? xtz.RevealFee.ToTez()
                : 0;

            if (type.HasFlag(BlockchainTransactionType.SwapPayment))
                return xtz.InitiateFee.ToTez() + revealFeeInTez;

            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return xtz.RefundFee.ToTez() + revealFeeInTez;

            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return xtz.RedeemFee.ToTez() + revealFeeInTez;

            return xtz.Fee.ToTez() + revealFeeInTez;
        }

        private decimal ReserveFee()
        {
            var xtz = Config;

            return new[]
                {
                    (xtz.RedeemFee + Math.Max((xtz.RedeemStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0)).ToTez(),
                    (xtz.RefundFee + Math.Max((xtz.RefundStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0)).ToTez()
                
                }.Max() + xtz.RevealFee.ToTez() + Config.MicroTezReserve.ToTez();
        }

        private async Task<decimal> StorageFeeByTypeAsync(
            BlockchainTransactionType type,
            string to,
            CancellationToken cancellationToken = default)
        {
            var xtz = Config;

            var isActive = await IsAllocatedDestinationAsync(to, cancellationToken)
                .ConfigureAwait(false);

            if (type.HasFlag(BlockchainTransactionType.SwapPayment))
                return (xtz.InitiateStorageLimit * xtz.StorageFeeMultiplier).ToTez();

            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return (isActive
                    ? Math.Max((xtz.RefundStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0) // without activation storage fee
                    : xtz.RefundStorageLimit * xtz.StorageFeeMultiplier)
                    .ToTez();

            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return (isActive
                    ? Math.Max((xtz.RedeemStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0) // without activation storage fee
                    : xtz.RedeemStorageLimit * xtz.StorageFeeMultiplier)
                    .ToTez();

            return (isActive
                ? Math.Max((xtz.StorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0) // without activation storage fee
                : xtz.StorageLimit * xtz.StorageFeeMultiplier)
                .ToTez();
        }

        public async Task<bool> IsRevealedSourceAsync(
            string from,
            CancellationToken cancellationToken = default)
        {
            return !string.IsNullOrEmpty(from) && await _tezosRevealChecker
                .IsRevealedAsync(from, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<bool> IsAllocatedDestinationAsync(
            string to,
            CancellationToken cancellationToken = default)
        {
            return !string.IsNullOrEmpty(to) && await _tezosAllocationChecker
                .IsAllocatedAsync(to, cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion Common

        #region Balances

        public override async Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            var scanner = new TezosWalletScanner(this);

            await scanner
                .UpdateBalanceAsync(skipUsed: false, cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var scanner = new TezosWalletScanner(this);

            await scanner
                .UpdateBalanceAsync(address, cancellationToken)
                .ConfigureAwait(false);
        }

        public void RaiseTokensBalanceUpdatedEvent(string address, string tokenContract, int? tokenId)
        {
            RaiseBalanceUpdated(new CurrencyEventArgs(
                address: address,
                tokenContract: tokenContract,
                tokenId: tokenId));
        }

        #endregion Balances

        #region Addresses

        public async Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = await GetUnspentAddressesAsync(cancellationToken)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return unspentAddresses.MaxBy(w => w.AvailableBalance());

            foreach (var chain in new[] {Bip44.Internal, Bip44.External})
            {
                var lastActiveAddress = await LocalStorage
                    .GetLastActiveWalletAddressAsync(
                        currency: Currency,
                        chain: chain,
                        keyType: CurrencyConfig.StandardKey)
                    .ConfigureAwait(false);

                if (lastActiveAddress != null)
                    return lastActiveAddress;
            }

            return await GetFreeExternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<IEnumerable<WalletAddress>> GetUnspentTokenAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return (await LocalStorage
                .GetTezosTokenAddressesAsync()
                .ConfigureAwait(false))
                .Where(w => w.AvailableBalance() > 0)
                .ToList();
        }

        public async Task<SelectedWalletAddress> CalculateFundsUsageAsync(
            string from,
            string to,
            decimal amount,
            decimal fee,
            FeeUsagePolicy feeUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default)
        {
            var xtz = Config;
            
            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return null; // invalid address

            var txFeeInTez = feeUsagePolicy == FeeUsagePolicy.EstimatedFee
                ? await FeeByType(
                        type: transactionType,
                        from: fromAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
                : fee;

            var storageFeeInTez = await StorageFeeByTypeAsync(
                    type: transactionType,
                    to: to,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var restBalanceInTez = fromAddress.AvailableBalance() -
                amount -
                txFeeInTez -
                storageFeeInTez -
                xtz.MicroTezReserve.ToTez();

            if (restBalanceInTez < 0)
                return null; // insufficient funds

            return new SelectedWalletAddress
            {
                WalletAddress  = fromAddress,
                UsedAmount     = amount,
                UsedFee        = txFeeInTez,
                UsedStorageFee = storageFeeInTez
            };
        }

        #endregion Addresses

        #region Transactions

        public override async Task<IEnumerable<IBlockchainTransaction>> GetUnconfirmedTransactionsAsync(
            CancellationToken cancellationToken = default)
        {
            return await LocalStorage
                .GetUnconfirmedTransactionsAsync<TezosTransaction>(Currency)
                .ConfigureAwait(false);
        }

        #endregion Transactions
    }
}