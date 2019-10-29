using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;
using Serilog;

namespace Atomex.Wallet.Tezos
{
    public class TezosCurrencyAccount : CurrencyAccount
    {
        private readonly TezosActivationChecker _tezosActivationChecker;

        public TezosCurrencyAccount(
            Currency currency,
            IHdWallet wallet,
            IAccountDataRepository dataRepository)
                : base(currency, wallet, dataRepository)
        {
            _tezosActivationChecker = new TezosActivationChecker(currency);
        }

        #region Common

        private Atomex.Tezos Xtz => (Atomex.Tezos) Currency;

        public override async Task<Error> SendAsync(
            IEnumerable<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default)
        {
            var selectedAddresses = (await SelectUnspentAddressesAsync(
                    from: from.ToList(),
                    to: to,
                    amount: amount,
                    fee: fee,
                    feeUsagePolicy: FeeUsagePolicy.FeeForAllTransactions,
                    addressUsagePolicy: AddressUsagePolicy.UseMinimalBalanceFirst,
                    transactionType: BlockchainTransactionType.Output,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            if (!selectedAddresses.Any())
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: "Insufficient funds");

            var feePerTxInMtz = Math.Round(fee.ToMicroTez() / selectedAddresses.Count);

            // todo: min fee control

            foreach (var selectedAddress in selectedAddresses)
            {
                var addressAmountMtz = selectedAddress.UsedAmount.ToMicroTez();

                Log.Debug("Send {@amount} XTZ from address {@address} with available balance {@balance}",
                    addressAmountMtz,
                    selectedAddress.WalletAddress.Address,
                    selectedAddress.WalletAddress.AvailableBalance());

                var tx = new TezosTransaction
                {
                    Currency = Xtz,
                    CreationTime = DateTime.UtcNow,
                    From = selectedAddress.WalletAddress.Address,
                    To = to,
                    Amount = Math.Round(addressAmountMtz, 0),
                    Fee = feePerTxInMtz,
                    GasLimit = Xtz.GasLimit,
                    StorageLimit = Xtz.StorageLimit,
                    Type = BlockchainTransactionType.Output
                };

                var signResult = await Wallet
                    .SignAsync(tx, selectedAddress.WalletAddress, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                    return new Error(
                        code: Errors.TransactionSigningError,
                        description: "Transaction signing error");

                var broadcastResult = await Currency.BlockchainApi
                    .BroadcastAsync(tx, cancellationToken)
                    .ConfigureAwait(false);

                if (broadcastResult.HasError)
                    return broadcastResult.Error;

                var txId = broadcastResult.Value;

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

            await UpdateBalanceAsync(cancellationToken)
                .ConfigureAwait(false);

            return null;
        }

        public override async Task<Error> SendAsync(
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
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
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<decimal?> EstimateFeeAsync(
            string to,
            decimal amount,
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (!unspentAddresses.Any())
                return null; // insufficient funds

            var selectedAddresses = (await SelectUnspentAddressesAsync(
                    from: unspentAddresses,
                    to: to,
                    amount: amount,
                    fee: 0,
                    feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                    addressUsagePolicy: AddressUsagePolicy.UseMinimalBalanceFirst,
                    transactionType: type,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            if (!selectedAddresses.Any())
                return null; // insufficient funds

            return selectedAddresses.Sum(s => s.UsedFee);
        }

        public override async Task<(decimal, decimal)> EstimateMaxAmountToSendAsync(
            string to,
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (!unspentAddresses.Any())
                return (0m, 0m);

            // minimum balance first
            unspentAddresses = unspentAddresses
                .ToList()
                .SortList((a, b) => a.AvailableBalance()
                    .CompareTo(b.AvailableBalance()));

            var isFirstTx = true;
            var amount = 0m;
            var fee = 0m;

            foreach (var address in unspentAddresses)
            {
                var feeInTez = FeeByType(type, isFirstTx: isFirstTx);
                var storageFeeInTez = await StorageFeeByTypeAsync(
                        type: type,
                        to: to,
                        isFirstTx: isFirstTx,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var usedAmountInTez = Math.Max(address.AvailableBalance() - feeInTez - storageFeeInTez, 0);

                if (usedAmountInTez <= 0)
                    continue;

                amount += usedAmountInTez;
                fee += feeInTez;

                if (isFirstTx)
                    isFirstTx = false;
            }

            return (amount, fee);
        }

        protected override async Task ResolveTransactionTypeAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            if (!(tx is TezosTransaction xtzTx))
                throw new ArgumentException("Invalid tx type", nameof(tx));

            var isFromSelf = await IsSelfAddressAsync(
                    address: xtzTx.From,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var isToSelf = await IsSelfAddressAsync(
                    address: xtzTx.To,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (isFromSelf)
                xtzTx.Type |= BlockchainTransactionType.Output;

            if (isToSelf)
                xtzTx.Type |= BlockchainTransactionType.Input;

            var oldTx = !xtzTx.IsInternal
                ? await DataRepository
                    .GetTransactionByIdAsync(Currency, tx.Id)
                    .ConfigureAwait(false)
                : null;

            if (oldTx != null)
                xtzTx.Type |= oldTx.Type;
            
            // todo: recognize swap payment/refund/redeem

            xtzTx.InternalTxs?.ForEach(async t => await ResolveTransactionTypeAsync(t, cancellationToken)
                .ConfigureAwait(false));
        }

        private decimal FeeByType(BlockchainTransactionType type, bool isFirstTx)
        {
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && isFirstTx)
                return Xtz.InitiateFee.ToTez();
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && !isFirstTx)
                return Xtz.AddFee.ToTez();
            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return Xtz.RefundFee.ToTez();
            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return Xtz.RedeemFee.ToTez();

            return Xtz.Fee.ToTez();
        }

        private async Task<decimal> StorageFeeByTypeAsync(
            BlockchainTransactionType type,
            string to,
            bool isFirstTx,
            CancellationToken cancellationToken = default)
        {
            var isActive = await IsActiveDestinationAsync(type, to, cancellationToken)
                .ConfigureAwait(false);

            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && isFirstTx)
                return Xtz.InitiateStorageLimit / 1000;
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && !isFirstTx)
                return Xtz.AddStorageLimit / 1000;
            if(type.HasFlag(BlockchainTransactionType.SwapRefund))
                return Xtz.RefundStorageLimit / 1000;
            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return isActive
                    ? Math.Max((Xtz.RedeemStorageLimit - Xtz.ActivationStorage) / 1000, 0) // without activation storage fee
                    : Xtz.RedeemStorageLimit / 1000;

            return isActive || !isFirstTx
                ? Math.Max((Xtz.StorageLimit - Xtz.ActivationStorage) / 1000, 0) // without activation storage fee
                : Xtz.StorageLimit / 1000;
        }

        private async Task<bool> IsActiveDestinationAsync(
            BlockchainTransactionType type,
            string to,
            CancellationToken cancellationToken = default)
        {
            if (to != null)
            {
                return await _tezosActivationChecker
                    .IsActivateAsync(to, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (type == BlockchainTransactionType.SwapRedeem || type == BlockchainTransactionType.SwapRefund)
            {
                var redeemAddress = await GetRedeemAddressAsync(cancellationToken)
                    .ConfigureAwait(false);

                return await _tezosActivationChecker
                    .IsActivateAsync(redeemAddress.Address, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (type == BlockchainTransactionType.SwapRefund)
            {
                var refundAddress = await GetRefundAddressAsync(cancellationToken)
                    .ConfigureAwait(false);

                return await _tezosActivationChecker
                    .IsActivateAsync(refundAddress.Address, cancellationToken)
                    .ConfigureAwait(false);
            }

            return false;
        }

        #endregion Common

        #region Balances

        public override async Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            var txs = (await DataRepository
                .GetTransactionsAsync(Currency)
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

                if (tx.Type.HasFlag(BlockchainTransactionType.Output))
                    selfAddresses.Add(tx.From);

                if (tx.Type.HasFlag(BlockchainTransactionType.Input))
                    selfAddresses.Add(tx.To);

                foreach (var address in selfAddresses)
                {
                    var isIncome = address == tx.To;
                    var isOutcome = address == tx.From;
                    var isConfirmed = tx.IsConfirmed;
                    var isFailed = tx.State == BlockchainTransactionState.Failed;

                    var income = isIncome && !isFailed
                        ? Atomex.Tezos.MtzToTz(tx.Amount)
                        : 0;

                    var outcome = isOutcome && !isFailed
                        ? -Atomex.Tezos.MtzToTz(tx.Amount + tx.Fee + tx.Burn)
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
            var api = (ITezosBlockchainApi)Xtz.BlockchainApi;

            foreach (var wa in addresses.Values)
            {
                var balanceResult = await api
                    .GetBalanceAsync(
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

                wa.Balance = balanceResult.Value.ToTez();

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

        public override async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await DataRepository
                .GetWalletAddressAsync(Currency, address)
                .ConfigureAwait(false);

            if (walletAddress == null)
                return;

            var api = (ITezosBlockchainApi) Xtz.BlockchainApi;

            var balanceResult = await api
                .GetBalanceAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (balanceResult.HasError)
            {
                Log.Error("Error while balance update for {@address} with code {@code} and description {@description}",
                    address,
                    balanceResult.Error.Code,
                    balanceResult.Error.Description);
                return;
            }
                
            var balance = balanceResult.Value.ToTez();

            // calculate unconfirmed balances
            var unconfirmedTxs = (await DataRepository
                .GetUnconfirmedTransactionsAsync(Currency)
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
                    ? Atomex.Tezos.MtzToTz(utx.Amount)
                    : 0;
                unconfirmedOutcome += address == utx.From && !isFailed
                    ? -Atomex.Tezos.MtzToTz(utx.Amount + utx.Fee + utx.Burn)
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

        #endregion Balances

        #region Addresses

        public override async Task<WalletAddress> GetRefundAddressAsync(
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
                        chain: chain)
                    .ConfigureAwait(false);

                if (lastActiveAddress != null)
                    return ResolvePublicKey(lastActiveAddress);
            }

            return await base.GetRefundAddressAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<WalletAddress> GetRedeemAddressAsync(
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
                        chain: chain)
                    .ConfigureAwait(false);

                if (lastActiveAddress != null)
                    return ResolvePublicKey(lastActiveAddress);
            }

            return await base.GetRedeemAddressAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
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

        private async Task<IEnumerable<SelectedWalletAddress>> SelectUnspentAddressesAsync(
            IList<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default)
        {
            if (addressUsagePolicy == AddressUsagePolicy.UseMinimalBalanceFirst)
            {
                from = from.ToList().SortList((a, b) => a.AvailableBalance().CompareTo(b.AvailableBalance()));
            }
            else if (addressUsagePolicy == AddressUsagePolicy.UseMaximumBalanceFirst)
            {
                from = from.ToList().SortList((a, b) => b.AvailableBalance().CompareTo(a.AvailableBalance()));
            }
            else if (addressUsagePolicy == AddressUsagePolicy.UseOnlyOneAddress)
            {
                var feeInTez = feeUsagePolicy == FeeUsagePolicy.EstimatedFee
                    ? FeeByType(transactionType, isFirstTx: true)
                    : fee;

                var storageFeeInTez = await StorageFeeByTypeAsync(
                        type: transactionType,
                        to: to,
                        isFirstTx: true,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var requiredAmountInTez = amount + feeInTez + storageFeeInTez;

                var address = from.FirstOrDefault(w => w.AvailableBalance() >= requiredAmountInTez);

                return address != null
                    ? new List<SelectedWalletAddress> {
                        new SelectedWalletAddress
                        {
                            WalletAddress = address,
                            UsedAmount = amount,
                            UsedFee = feeInTez // without activation fee and storage fee
                        }
                    }
                    : Enumerable.Empty<SelectedWalletAddress>();
            }

            for (var txCount = 1; txCount <= from.Count; ++txCount)
            {
                var result = new List<SelectedWalletAddress>();
                var requiredAmount = amount;

                var isFirstTx = true;
                var completed = false;

                foreach (var address in from)
                {
                    var availableBalanceInTez = address.AvailableBalance();

                    var txFeeInTez = feeUsagePolicy == FeeUsagePolicy.EstimatedFee
                        ? FeeByType(transactionType, isFirstTx)
                        : (feeUsagePolicy == FeeUsagePolicy.FeeForAllTransactions
                            ? Math.Round(fee / txCount, Xtz.Digits)
                            : fee);

                    var storageFeeInTez = await StorageFeeByTypeAsync(
                            type: transactionType,
                            to: to,
                            isFirstTx: isFirstTx,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (availableBalanceInTez <= txFeeInTez + storageFeeInTez) // ignore address with balance less than fee
                        continue;

                    var amountToUse = Math.Min(Math.Max(availableBalanceInTez - txFeeInTez - storageFeeInTez, 0), requiredAmount);

                    result.Add(new SelectedWalletAddress
                    {
                        WalletAddress = address,
                        UsedAmount = amountToUse,
                        UsedFee = txFeeInTez // without activation fee and storage fee
                    });
                    requiredAmount -= amountToUse;

                    if (requiredAmount <= 0)
                    {
                        completed = true;
                        break;
                    }

                    if (result.Count == txCount) // will need more transactions
                        break;

                    if (isFirstTx)
                        isFirstTx = false;
                }

                if (completed)
                    return result;
            }

            return Enumerable.Empty<SelectedWalletAddress>();
        }

        #endregion Addresses
    }
}