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
    public class TezosAccount : CurrencyAccount, ILegacyCurrencyAccount
    {
        private readonly TezosRevealChecker _tezosRevealChecker;
        private readonly TezosAllocationChecker _tezosAllocationChecker;

        public readonly ResourceLocker<string> AddressLocker;

        public TezosAccount(
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository)
                : base(TezosConfig.Xtz, currencies, wallet, dataRepository)
        {
            var xtz = Config;

            _tezosRevealChecker = new TezosRevealChecker(xtz);
            _tezosAllocationChecker = new TezosAllocationChecker(xtz);

            AddressLocker = new ResourceLocker<string>();
        }

        #region Common

        public TezosConfig Config => Currencies.Get<TezosConfig>(Currency);

        public async Task<Error> SendAsync(
            IEnumerable<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool useDefaultFee = true,
            CancellationToken cancellationToken = default)
        {
            var xtzConfig = Config;

            var fromAddresses = from
                .Where(w => w.Address != to) // filter self address usage
                .ToList();

            var selectedAddresses = (await SelectUnspentAddressesAsync(
                    from: fromAddresses,
                    to: to,
                    amount: amount,
                    fee: fee,
                    feeUsagePolicy: useDefaultFee ? FeeUsagePolicy.EstimatedFee : FeeUsagePolicy.FeeForAllTransactions,
                    addressUsagePolicy: AddressUsagePolicy.UseMinimalBalanceFirst,
                    transactionType: BlockchainTransactionType.Output,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            if (!selectedAddresses.Any())
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: "Insufficient funds");

            var isActive = await IsAllocatedDestinationAsync(to, cancellationToken)
                .ConfigureAwait(false);

            // todo: min fee control
            var isFirstTx = true;

            foreach (var selectedAddress in selectedAddresses)
            {
                var addressAmountMtz = selectedAddress.UsedAmount.ToMicroTez();

                Log.Debug("Send {@amount} XTZ from address {@address} with available balance {@balance}",
                    addressAmountMtz,
                    selectedAddress.WalletAddress.Address,
                    selectedAddress.WalletAddress.AvailableBalance());

                var storageLimit = isActive || !isFirstTx
                    ? Math.Max(xtzConfig.StorageLimit - xtzConfig.ActivationStorage, 0) // without activation storage fee
                    : xtzConfig.StorageLimit;

                var tx = new TezosTransaction
                {
                    Currency      = xtzConfig.Name,
                    CreationTime  = DateTime.UtcNow,
                    From          = selectedAddress.WalletAddress.Address,
                    To            = to,
                    Amount        = Math.Round(addressAmountMtz, 0),
                    Fee           = selectedAddress.UsedFee.ToMicroTez(),
                    GasLimit      = xtzConfig.GasLimit,
                    StorageLimit  = storageLimit,
                    Type          = BlockchainTransactionType.Output,

                    UseRun              = useDefaultFee,
                    UseSafeStorageLimit = false,
                    UseOfflineCounter   = true
                };

                string txId = null;

                try
                {
                    await AddressLocker
                        .LockAsync(selectedAddress.WalletAddress.Address, cancellationToken)
                        .ConfigureAwait(false);

                    using var securePublicKey = Wallet.GetPublicKey(
                        currency: xtzConfig,
                        keyIndex: selectedAddress.WalletAddress.KeyIndex,
                        keyType: selectedAddress.WalletAddress.KeyType);

                    // fill operation
                    var fillResult = await tx
                        .FillOperationsAsync(
                            securePublicKey: securePublicKey,
                            tezosConfig: xtzConfig,
                            headOffset: TezosConfig.HeadOffset,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    var signResult = await Wallet
                        .SignAsync(tx, selectedAddress.WalletAddress, xtzConfig, cancellationToken)
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

                    txId = broadcastResult.Value;

                    if (txId == null)
                        return new Error(
                            code: Errors.TransactionBroadcastError,
                            description: "Transaction Id is null");

                    Log.Debug("Transaction successfully sent with txId: {@id}", txId);

                    await UpsertTransactionAsync(
                            tx: tx,
                            updateBalance: false,
                            notifyIfUnconfirmed: true,
                            notifyIfBalanceUpdated: false,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    AddressLocker.Unlock(selectedAddress.WalletAddress.Address);
                }

                if (txId != null && isFirstTx && selectedAddresses.Count > 1)
                {
                    isFirstTx = false;
                
                    if (!isActive)
                    {
                        // delay for waiting confirmation
                        var isConfirmed = await WaitFirstTxConfirmationAsync(txId, TimeSpan.FromMinutes(3), cancellationToken)
                            .ConfigureAwait(false);

                        if (!isConfirmed)
                        {
                            return new Error(
                                code: Errors.TransactionBroadcastError,
                                description: $"Transaction {txId} has not be confirmed for a long time");
                        }
                    }
                }
            }

            return null;
        }

        private async Task<bool> WaitFirstTxConfirmationAsync(
            string txId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var timeStamp = DateTime.UtcNow;

            while (DateTime.UtcNow < timeStamp + timeout)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken)
                    .ConfigureAwait(false);

                var tx = await Config.BlockchainApi
                    .TryGetTransactionAsync(txId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (tx != null && !tx.HasError && tx.Value != null && tx.Value.State == BlockchainTransactionState.Confirmed)
                    return true;
            }

            return false;
        }

        public async Task<Error> SendAsync(
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool useDefaultFee = true,
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            return await SendAsync(
                    from: unspentAddresses,
                    to: to,
                    amount: amount,
                    fee: fee,
                    feePrice: feePrice,
                    useDefaultFee: useDefaultFee,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<decimal?> EstimateFeeAsync(
            string to,
            decimal amount,
            BlockchainTransactionType type,
            decimal fee = 0,
            decimal feePrice = 0,
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (!type.HasFlag(BlockchainTransactionType.SwapRedeem) &&
                !type.HasFlag(BlockchainTransactionType.SwapRefund))
            {
                unspentAddresses = unspentAddresses
                    .Where(w => w.Address != to)
                    .ToList();
            }

            if (!unspentAddresses.Any())
                return null; // insufficient funds

            var selectedAddresses = (await SelectUnspentAddressesAsync(
                    from: unspentAddresses,
                    to: to,
                    amount: amount,
                    fee: fee,
                    feeUsagePolicy: fee == 0 ? FeeUsagePolicy.EstimatedFee : FeeUsagePolicy.FeeForAllTransactions,
                    addressUsagePolicy: AddressUsagePolicy.UseMinimalBalanceFirst,
                    transactionType: type,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            if (!selectedAddresses.Any())
                return null; // insufficient funds

            return selectedAddresses.Sum(s => s.UsedFee);
        }
        
        public async Task<(decimal, decimal, decimal)> EstimateMaxAmountToSendAsync(
            string to,
            BlockchainTransactionType type,
            decimal feeAmount = 0,
            decimal feePrice = 0,
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (!type.HasFlag(BlockchainTransactionType.SwapRedeem) &&
                !type.HasFlag(BlockchainTransactionType.SwapRefund))
            {
                unspentAddresses = unspentAddresses
                    .Where(w => w.Address != to)
                    .ToList();
            }

            if (!unspentAddresses.Any())
                return (0m, 0m, 0m);

            // minimum balance first
            unspentAddresses = unspentAddresses
                .ToList()
                .SortList(new AvailableBalanceAscending());

            var isFirstTx = true;
            var amount = 0m;
            var fee = 0m;

            var reserveFee = ReserveFee();

            foreach (var address in unspentAddresses)
            {
                var feeInTez = await FeeByType(
                        type: type,
                        from: address.Address,
                        isFirstTx: isFirstTx,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var storageFeeInTez = await StorageFeeByTypeAsync(
                        type: type,
                        to: to,
                        isFirstTx: isFirstTx,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var usedAmountInTez = Math.Max(address.AvailableBalance() - feeInTez - storageFeeInTez - (reserve && address == unspentAddresses.Last() ? reserveFee : 0) - Config.MicroTezReserve.ToTez(), 0);

                if (usedAmountInTez <= 0)
                    continue;

                amount += usedAmountInTez;
                fee += feeInTez;

                if (isFirstTx)
                    isFirstTx = false;
            }

            return (amount, fee, reserveFee);
        }

        protected override async Task<bool> ResolveTransactionTypeAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var xtz = Config;

            if (!(tx is TezosTransaction xtzTx))
                throw new ArgumentException("Invalid tx type", nameof(tx));

            var oldTx = !xtzTx.IsInternal
                ? await DataRepository
                    .GetTransactionByIdAsync(Currency, tx.Id, Config.TransactionType)
                    .ConfigureAwait(false)
                : null;

            if (oldTx != null && oldTx.IsConfirmed)
                return false;

            var isFromSelf = await IsSelfAddressAsync(
                    address: xtzTx.From,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (isFromSelf)
            { 
                xtzTx.Type |= BlockchainTransactionType.Output;

                var isToSwapContract = xtzTx.To == xtz.SwapContractAddress;

                if (isToSwapContract)
                {
                    // todo: recognize swap payment/refund/redeem
                }
                else if (xtzTx.Amount == 0)
                {
                    xtzTx = ResolveFA12TransactionType(xtzTx);
                }
            }

            var isToSelf = await IsSelfAddressAsync(
                    address: xtzTx.To,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (isToSelf)
                xtzTx.Type |= BlockchainTransactionType.Input;

            if (oldTx != null)
                xtzTx.Type |= oldTx.Type;

            // todo: recognize swap payment/refund/redeem

            xtzTx.InternalTxs?
                .ForEach(async t => await ResolveTransactionTypeAsync(t, cancellationToken)
                .ConfigureAwait(false));

            ResolveTezosTxAlias(xtzTx);

            return true;
        }

        public static void ResolveTezosTxAlias(TezosTransaction tx)
        {
            var ALIAS_DELIMETER = '/';

            if (string.IsNullOrEmpty(tx.Alias) || tx.Alias.IndexOf(ALIAS_DELIMETER) == -1)
                return;

            if (tx.Type.HasFlag(BlockchainTransactionType.Input))
            {
                tx.Alias = tx.Alias.Split(ALIAS_DELIMETER)[0];
            }
            else if (tx.Type.HasFlag(BlockchainTransactionType.Output))
            {
                tx.Alias = tx.Alias.Split(ALIAS_DELIMETER)[1];
            }
        }

        private TezosTransaction ResolveFA12TransactionType(
            TezosTransaction tx)
        {
            if (tx.Params["entrypoint"].ToString().Equals("initiate")
                || tx.Params["entrypoint"].ToString().Equals("redeem")
                || tx.Params["entrypoint"].ToString().Equals("refund"))
                tx.Type |= BlockchainTransactionType.SwapCall;
            else if (tx.Params["entrypoint"].ToString().Equals("transfer")
                || tx.Params["entrypoint"].ToString().Equals("approve"))
                tx.Type |= BlockchainTransactionType.TokenCall;

            return tx;
        }

        private async Task<decimal> FeeByType(
            BlockchainTransactionType type,
            string from,
            bool isFirstTx,
            CancellationToken cancellationToken = default)
        {
            var xtz = Config;

            var isRevealed = await IsRevealedSourceAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && isFirstTx)
                return xtz.InitiateFee.ToTez() + (isRevealed ? 0 : xtz.RevealFee.ToTez());
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && !isFirstTx)
                return xtz.AddFee.ToTez() + (isRevealed ? 0 : xtz.RevealFee.ToTez());
            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return xtz.RefundFee.ToTez() + (isRevealed ? 0 : xtz.RevealFee.ToTez());
            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return xtz.RedeemFee.ToTez() + (isRevealed ? 0 : xtz.RevealFee.ToTez());

            return xtz.Fee.ToTez() + (isRevealed ? 0 : xtz.RevealFee.ToTez());
        }

        private decimal ReserveFee()
        {
            //var fa12 = Fa12;
            var xtz = Config;

            return new[] {
                //(fa12.RedeemFee + Math.Max((fa12.RedeemStorageLimit - fa12.ActivationStorage) / fa12.StorageFeeMultiplier, 0)).ToTez(),
                //(fa12.RefundFee + Math.Max((fa12.RefundStorageLimit - fa12.ActivationStorage) / fa12.StorageFeeMultiplier, 0)).ToTez(),
                (xtz.RedeemFee + Math.Max((xtz.RedeemStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0)).ToTez(),
                (xtz.RefundFee + Math.Max((xtz.RefundStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0)).ToTez()
            }.Max() + xtz.RevealFee.ToTez() + Config.MicroTezReserve.ToTez();
        }

        private async Task<decimal> StorageFeeByTypeAsync(
            BlockchainTransactionType type,
            string to,
            bool isFirstTx,
            CancellationToken cancellationToken = default)
        {
            var xtz = Config;

            var isActive = await IsAllocatedDestinationAsync(to, cancellationToken)
                .ConfigureAwait(false);

            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && isFirstTx)
                return (xtz.InitiateStorageLimit * xtz.StorageFeeMultiplier).ToTez();
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && !isFirstTx)
                return (xtz.AddStorageLimit * xtz.StorageFeeMultiplier).ToTez();
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

            return (isActive || !isFirstTx
                ? Math.Max((xtz.StorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0) // without activation storage fee
                : xtz.StorageLimit * xtz.StorageFeeMultiplier)
                .ToTez();
        }

        public async Task<bool> IsRevealedSourceAsync(
            string from,
            CancellationToken cancellationToken = default)
        {
            return from != null && await _tezosRevealChecker
                .IsRevealedAsync(from, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<bool> IsAllocatedDestinationAsync(
            string to,
            CancellationToken cancellationToken = default)
        {
            return to != null && await _tezosAllocationChecker
                .IsAllocatedAsync(to, cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion Common

        #region Balances

        public override Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var xtz = Config;

                    var txs = (await DataRepository
                        .GetTransactionsAsync(Currency, xtz.TransactionType)
                        .ConfigureAwait(false))
                        .Cast<TezosTransaction>()
                        .ToList();

                    var internalTxs = txs.Aggregate(new List<TezosTransaction>(), (list, tx) =>
                    {
                        if (tx.InternalTxs != null)
                            list.AddRange(tx.InternalTxs);

                        return list;
                    });

                    // calculate unconfirmed balances
                    var totalUnconfirmedIncome = 0m;
                    var totalUnconfirmedOutcome = 0m;

                    var addresses = new Dictionary<string, WalletAddress>();

                    foreach (var tx in txs.Concat(internalTxs))
                    {
                        var selfAddresses = new HashSet<string>();

                        var isFromSelf = await IsSelfAddressAsync(tx.From, cancellationToken)
                            .ConfigureAwait(false);

                        //if (tx.Type.HasFlag(BlockchainTransactionType.Output))
                        if (isFromSelf)
                            selfAddresses.Add(tx.From);

                        var isToSelf = await IsSelfAddressAsync(tx.To, cancellationToken)
                            .ConfigureAwait(false);

                        //if (tx.Type.HasFlag(BlockchainTransactionType.Input))
                        if (isToSelf)
                            selfAddresses.Add(tx.To);

                        foreach (var address in selfAddresses)
                        {
                            var isIncome = address == tx.To;
                            var isOutcome = address == tx.From;
                            var isConfirmed = tx.IsConfirmed;
                            var isFailed = tx.State == BlockchainTransactionState.Failed;

                            var income = isIncome && !isFailed
                                ? TezosConfig.MtzToTz(tx.Amount)
                                : 0;

                            var outcome = isOutcome && !isFailed
                                ? -TezosConfig.MtzToTz(tx.Amount + tx.Fee + tx.Burn)
                                : 0;

                            if (addresses.TryGetValue(address, out var walletAddress))
                            {
                                walletAddress.UnconfirmedIncome += !isConfirmed ? income : 0;
                                walletAddress.UnconfirmedOutcome += !isConfirmed ? outcome : 0;
                            }
                            else
                            {
                                walletAddress = await DataRepository
                                    .GetWalletAddressAsync(Currency, address)
                                    .ConfigureAwait(false);

                                if (walletAddress == null)
                                    continue;

                                walletAddress.UnconfirmedIncome = !isConfirmed ? income : 0;
                                walletAddress.UnconfirmedOutcome = !isConfirmed ? outcome : 0;
                                walletAddress.HasActivity = true;

                                addresses.Add(address, walletAddress);
                            }

                            totalUnconfirmedIncome += !isConfirmed ? income : 0;
                            totalUnconfirmedOutcome += !isConfirmed ? outcome : 0;
                        }
                    }

                    var totalBalance = 0m;

                    foreach (var wa in addresses.Values)
                    {
                        var balanceResult = await xtz.BlockchainApi
                            .TryGetBalanceAsync(
                                address: wa.Address,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (balanceResult.HasError)
                        {
                            Log.Error("Error while getting balance for {@address} with code {@code} and description {@description}",
                                wa.Address,
                                balanceResult.Error.Code,
                                balanceResult.Error.Description);

                            continue; // todo: may be return?
                        }

                        wa.Balance = balanceResult.Value; //.ToTez();

                        totalBalance += wa.Balance;
                    }

                    // upsert addresses
                    await DataRepository
                        .UpsertAddressesAsync(addresses.Values)
                        .ConfigureAwait(false);

                    Balance = totalBalance;
                    UnconfirmedIncome = totalUnconfirmedIncome;
                    UnconfirmedOutcome = totalUnconfirmedOutcome;

                    RaiseBalanceUpdated(new CurrencyEventArgs(Currency));
                }
                catch (OperationCanceledException)
                {
                    Log.Debug($"{Currency} UpdateBalanceAsync canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, "Tezos UpdateBalanceAsync error.");
                }

            }, cancellationToken);
        }

        public override Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var xtz = Config;

                    var walletAddress = await DataRepository
                        .GetWalletAddressAsync(Currency, address)
                        .ConfigureAwait(false);

                    if (walletAddress == null)
                        return;

                    var balanceResult = await xtz.BlockchainApi
                        .TryGetBalanceAsync(address, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (balanceResult.HasError)
                    {
                        Log.Error("Error while balance update for {@address} with code {@code} and description {@description}",
                            address,
                            balanceResult.Error.Code,
                            balanceResult.Error.Description);
                        return;
                    }

                    var balance = balanceResult.Value; //.ToTez();

                    // calculate unconfirmed balances
                    var unconfirmedTxs = (await DataRepository
                        .GetUnconfirmedTransactionsAsync(Currency, xtz.TransactionType)
                        .ConfigureAwait(false))
                        .Cast<TezosTransaction>()
                        .ToList();

                    var unconfirmedInternalTxs = unconfirmedTxs.Aggregate(new List<TezosTransaction>(), (list, tx) =>
                    {
                        if (tx.InternalTxs != null)
                            list.AddRange(tx.InternalTxs);

                        return list;
                    });

                    var unconfirmedIncome = 0m;
                    var unconfirmedOutcome = 0m;

                    foreach (var utx in unconfirmedTxs.Concat(unconfirmedInternalTxs))
                    {
                        var isFailed = utx.State == BlockchainTransactionState.Failed;

                        unconfirmedIncome += address == utx.To && !isFailed
                            ? TezosConfig.MtzToTz(utx.Amount)
                            : 0;
                        unconfirmedOutcome += address == utx.From && !isFailed
                            ? -TezosConfig.MtzToTz(utx.Amount + utx.Fee + utx.Burn)
                            : 0;
                    }

                    var balanceDifference = balance - walletAddress.Balance;
                    var unconfirmedIncomeDifference = unconfirmedIncome - walletAddress.UnconfirmedIncome;
                    var unconfirmedOutcomeDifference = unconfirmedOutcome - walletAddress.UnconfirmedOutcome;

                    if (balanceDifference != 0 ||
                        unconfirmedIncomeDifference != 0 ||
                        unconfirmedOutcomeDifference != 0)
                    {
                        walletAddress.Balance = balance;
                        walletAddress.UnconfirmedIncome = unconfirmedIncome;
                        walletAddress.UnconfirmedOutcome = unconfirmedOutcome;
                        walletAddress.HasActivity = true;

                        await DataRepository.UpsertAddressAsync(walletAddress)
                            .ConfigureAwait(false);

                        Balance += balanceDifference;
                        UnconfirmedIncome += unconfirmedIncomeDifference;
                        UnconfirmedOutcome += unconfirmedOutcomeDifference;

                        RaiseBalanceUpdated(new CurrencyEventArgs(Currency));
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug($"{Currency} UpdateBalanceAsync canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, "Tezos UpdateBalanceAsync error.");
                }

            }, cancellationToken);
        }

        #endregion Balances

        #region Addresses

        public async Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = await GetUnspentAddressesAsync(cancellationToken)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return ResolvePublicKey(unspentAddresses.MaxBy(w => w.AvailableBalance()));

            foreach (var chain in new[] {Bip44.Internal, Bip44.External})
            {
                var lastActiveAddress = await DataRepository
                    .GetLastActiveWalletAddressAsync(
                        currency: Currency,
                        chain: chain,
                        keyType: CurrencyConfig.ClassicKey)
                    .ConfigureAwait(false);

                if (lastActiveAddress != null)
                    return ResolvePublicKey(lastActiveAddress);
            }

            var redeemAddress = await GetFreeExternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            return ResolvePublicKey(redeemAddress);
        }

        public async Task<IEnumerable<WalletAddress>> GetUnspentTokenAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return (await DataRepository
                .GetTezosTokenAddressesAsync()
                .ConfigureAwait(false))
                .Where(w => w.AvailableBalance() > 0)
                .ToList();
        }

        public async Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string toAddress,
            decimal amount,
            decimal fee,
            decimal feePrice,
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (!transactionType.HasFlag(BlockchainTransactionType.SwapRedeem) &&
                !transactionType.HasFlag(BlockchainTransactionType.SwapRefund))
            {
                unspentAddresses = unspentAddresses
                    .Where(w => w.Address != toAddress)
                    .ToList();
            }

            var selectedAddresses = await SelectUnspentAddressesAsync(
                    from: unspentAddresses,
                    to: toAddress,
                    amount: amount,
                    fee: fee,
                    feeUsagePolicy: feeUsagePolicy,
                    addressUsagePolicy: addressUsagePolicy,
                    transactionType: transactionType,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return ResolvePublicKeys(selectedAddresses
                .Select(w => w.WalletAddress)
                .ToList());
        }

        public async Task<IEnumerable<SelectedWalletAddress>> SelectUnspentAddressesAsync(
            IList<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default)
        {
            var xtz = Config;

            if (addressUsagePolicy == AddressUsagePolicy.UseMinimalBalanceFirst)
            {
                from = from.ToList().SortList(new AvailableBalanceAscending());
            }
            else if (addressUsagePolicy == AddressUsagePolicy.UseMaximumBalanceFirst)
            {
                from = from.ToList().SortList(new AvailableBalanceDescending());
            }
            else if (addressUsagePolicy == AddressUsagePolicy.UseOnlyOneAddress)
            {
                //"to" used for redeem and refund - using the destination address is prefered
                from = from.Where(a => a.Address == to).Concat(from.Where(a => a.Address != to)).ToList();
            }
            
            var result = new List<SelectedWalletAddress>();
            var requiredAmount = amount;

            if (feeUsagePolicy == FeeUsagePolicy.FeeForAllTransactions)
                requiredAmount += fee;

            var isFirstTx = true;
            var completed = false;

            foreach (var address in from)
            {
                var availableBalanceInTez = address.AvailableBalance();

                var txFeeInTez = feeUsagePolicy == FeeUsagePolicy.FeePerTransaction
                    ? fee
                    : await FeeByType(
                            type: transactionType,
                            from: address.Address,
                            isFirstTx: isFirstTx,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                var storageFeeInTez = await StorageFeeByTypeAsync(
                        type: transactionType,
                        to: to,
                        isFirstTx: isFirstTx,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var netAvailableBalanceInTez = availableBalanceInTez - txFeeInTez - storageFeeInTez - xtz.MicroTezReserve.ToTez();

                if (netAvailableBalanceInTez <= 0) // ignore address with balance less than fee
                    continue;

                if(addressUsagePolicy == AddressUsagePolicy.UseOnlyOneAddress)
                {
                    if (Math.Min(netAvailableBalanceInTez, requiredAmount) == requiredAmount)
                        return new List<SelectedWalletAddress> {
                            new SelectedWalletAddress
                            {
                                WalletAddress = address,
                                UsedAmount = amount,
                                UsedFee = txFeeInTez,
                                UsedStorageFee = storageFeeInTez
                            }
                        };
                    continue;
                }
                
                decimal amountToUse = 0;

                if (feeUsagePolicy == FeeUsagePolicy.EstimatedFee)
                {
                    amountToUse = Math.Min(netAvailableBalanceInTez, requiredAmount);
                    requiredAmount -= amountToUse;
                }
                else if (feeUsagePolicy == FeeUsagePolicy.FeeForAllTransactions)
                {
                    amountToUse = Math.Min(netAvailableBalanceInTez, requiredAmount - txFeeInTez);
                    requiredAmount -= (amountToUse + txFeeInTez);
                }
                
                if(amountToUse > 0)
                    result.Add(new SelectedWalletAddress
                    {
                        WalletAddress = address,
                        UsedAmount = amountToUse,
                        UsedFee = txFeeInTez,
                        UsedStorageFee = storageFeeInTez
                    });

                if (requiredAmount <= 0)
                {
                    completed = true;

                    if (feeUsagePolicy == FeeUsagePolicy.FeeForAllTransactions)
                    {
                        requiredAmount = amount;
                        var estimatedFee = result.Sum(s => s.UsedFee);
                        var remainingFee = fee - estimatedFee;

                        var extraFee = 0m;

                        if (remainingFee > 0)
                        {
                            var res = result.ToList();
                            result = new List<SelectedWalletAddress>();

                            foreach (var s in res)
                            {
                                extraFee = Math.Round(remainingFee * s.UsedFee / estimatedFee, xtz.Digits);

                                if (extraFee + requiredAmount <= s.UsedAmount)
                                {
                                    s.UsedAmount = requiredAmount;
                                    s.UsedFee += extraFee;
                                    result.Add(s);
                                    break;
                                }

                                if (s == res.Last())
                                {
                                    s.UsedAmount = requiredAmount;
                                    s.UsedFee += Math.Min(s.WalletAddress.AvailableBalance() - s.UsedAmount - s.UsedFee - s.UsedStorageFee - xtz.MicroTezReserve.ToTez(), remainingFee);
                                    if (s.WalletAddress.AvailableBalance() - s.UsedAmount - s.UsedFee - s.UsedStorageFee - xtz.MicroTezReserve.ToTez() < 0) //check if possible
                                    {
                                        Log.Error("Error in fee distribution for transactions, fee is {@fee} with used fee {@usedFee}",
                                            fee,
                                            s.UsedFee + s.UsedStorageFee);
                                    }
                                }
                                else
                                {
                                    if (extraFee >= s.UsedAmount)
                                    {
                                        //remainingFee -= s.UsedAmount; //todo: use it when replacing allocation fee
                                        //estimatedFee -= s.UsedFee;
                                        //continue;
                                        extraFee = Math.Min(s.UsedAmount - 1 / xtz.DigitsMultiplier, extraFee);
                                    }

                                    remainingFee -= extraFee;
                                    estimatedFee -= s.UsedFee;
                                    s.UsedAmount -= extraFee;
                                    s.UsedFee += extraFee;
                                }

                                requiredAmount -= Math.Min(requiredAmount, s.UsedAmount);

                                result.Add(s);

                                if (requiredAmount <= 0)
                                    break;
                            }
                        }
                        else //todo: delete
                        {
                            Log.Error("Error fee is too small for transaction, fee is {@fee} with estimated fee {@estimatedFee}",
                                fee,
                                estimatedFee);
                        }
                        
                    }

                    break;
                }
                if (isFirstTx)
                    isFirstTx = false;
            }
            if (completed)
                return result;

            if (feeUsagePolicy == FeeUsagePolicy.FeeForAllTransactions) //todo: delete
            {
                Log.Error("Error fee is too big for transaction, fee is {@fee} with estimated fee {@estimatedFee}",
                    fee,
                    result.Sum(s => s.UsedFee));
            }

            return Enumerable.Empty<SelectedWalletAddress>();
        }

        #endregion Addresses
    }
}