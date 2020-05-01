using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomex.Wallet.Tezos
{
    public class FA12Account : TezosAccount
    {
        public FA12Account(
            string currency,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository)
                : base(currency, currencies, wallet, dataRepository)
        {
        }

        #region Common

        private TezosTokens.FA12 Fa12 => Currencies.Get<TezosTokens.FA12>(Currency);
        private Atomex.Tezos Xtz => Currencies.Get<Atomex.Tezos>("XTZ");

        public override async Task<Error> SendAsync(
            IEnumerable<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool useDefaultFee = true,
            CancellationToken cancellationToken = default)
        {
            var fa12 = Fa12;

            var fromAddresses = from
                .Where(w => w.Address != to) // filter self address usage
                .ToList();

            var selectedAddresses = (await SelectUnspentAddressesAsync(
                    from: fromAddresses,
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

            var isActive = await IsAllocatedDestinationAsync(BlockchainTransactionType.Output, to, cancellationToken)
                .ConfigureAwait(false);

            // todo: min fee control
            var isFirstTx = true;

            foreach (var selectedAddress in selectedAddresses)
            {
                var addressAmountInDigits = selectedAddress.UsedAmount.ToTokenDigits(fa12.DigitsMultiplier);

                Log.Debug("Send {@amount} tokens from address {@address} with available balance {@balance}",
                    addressAmountInDigits,
                    selectedAddress.WalletAddress.Address,
                    selectedAddress.WalletAddress.AvailableBalance());

                var storageLimit = isActive || !isFirstTx
                    ? Math.Max(fa12.TransferStorageLimit - fa12.ActivationStorage, 0) // without activation storage fee
                    : fa12.TransferStorageLimit;

                var tx = new TezosTransaction
                {
                    Currency = fa12,
                    CreationTime = DateTime.UtcNow,
                    From = selectedAddress.WalletAddress.Address,
                    To = fa12.TokenContractAddress,
                    Fee = selectedAddress.UsedFee.ToMicroTez(),
                    GasLimit = fa12.TransferGasLimit,
                    StorageLimit = storageLimit,
                    Params = TransferParams(selectedAddress.WalletAddress.Address, to, Math.Round(addressAmountInDigits, 0)),
                    UseDefaultFee = useDefaultFee,
                    Type = BlockchainTransactionType.Output
                };

                var signResult = await Wallet
                    .SignAsync(tx, selectedAddress.WalletAddress, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                    return new Error(
                        code: Errors.TransactionSigningError,
                        description: "Transaction signing error");

                var broadcastResult = await fa12.BlockchainApi
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

                tx.Amount = Math.Round(addressAmountInDigits, 0);

                await UpsertTransactionAsync(
                        tx: tx,
                        updateBalance: false,
                        notifyIfUnconfirmed: true,
                        notifyIfBalanceUpdated: false,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (isFirstTx && selectedAddresses.Count > 1)
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

            UpdateBalanceAsync(cancellationToken)
                .FireAndForget();

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

                var tx = await Xtz.BlockchainApi
                    .TryGetTransactionAsync(txId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (tx != null && !tx.HasError && tx.Value != null && tx.Value.State == BlockchainTransactionState.Confirmed)
                    return true;
            }

            return false;
        }

        public override async Task<Error> SendAsync(
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

        public override async Task<decimal?> EstimateFeeAsync(
            string to,
            decimal amount,
            BlockchainTransactionType type,
            decimal inputFee = 0,
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
                    fee: inputFee,
                    feeUsagePolicy: inputFee == 0 ? FeeUsagePolicy.EstimatedFee : FeeUsagePolicy.FeeForAllTransactions,
                    addressUsagePolicy: AddressUsagePolicy.UseMinimalBalanceFirst,
                    transactionType: type,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            if (!selectedAddresses.Any())
                return null; // insufficient funds

            return selectedAddresses.Sum(s => s.UsedFee);
        }

        public override async Task<(decimal, decimal, decimal)> EstimateMaxAmountToSendAsync(
            string to,
            BlockchainTransactionType type,
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            var xtz = Xtz;

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

            var reserveFee = ReserveFeeByTypeAsync(
                type: type,
                cancellationToken: cancellationToken);

            foreach (var address in unspentAddresses)
            {
                var xtzAddress = await DataRepository
                    .GetWalletAddressAsync(xtz.Name, address.Address)
                    .ConfigureAwait(false);

                var availableBalanceInTez = xtzAddress.AvailableBalance();

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

                var usedAmountInTez = Math.Max(availableBalanceInTez - feeInTez - storageFeeInTez - ((reserve && address == unspentAddresses.Last()) ? reserveFee : 0) - xtz.MicroTezReserve.ToTez(), 0);

                if (usedAmountInTez <= 0)
                    continue;

                amount += address.AvailableBalance();
                fee += feeInTez;

                if (isFirstTx)
                    isFirstTx = false;
            }

            return (amount, fee, reserveFee);
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

            var oldTx = (TezosTransaction)await DataRepository
                .GetTransactionByIdAsync(Currency, tx.Id, Fa12.TransactionType)
                .ConfigureAwait(false);

            if (oldTx != null)
            {
                xtzTx.Type = oldTx.Type;

                if (xtzTx.IsInternal && oldTx.IsConfirmed)
                {
                    xtzTx.From = oldTx.From;
                    xtzTx.To = oldTx.To;
                    xtzTx.Fee = oldTx.Fee;
                    xtzTx.GasLimit = oldTx.GasLimit;
                }
            }

            // todo: recognize swap payment/refund/redeem

            xtzTx.InternalTxs?.ForEach(async t => await ResolveTransactionTypeAsync(t, cancellationToken)
                .ConfigureAwait(false));
        }

        private async Task<decimal> FeeByType(
            BlockchainTransactionType type,
            string from,
            bool isFirstTx,
            CancellationToken cancellationToken = default)
        {
            var fa12 = Fa12;

            var isRevealed = await IsRevealedSourceAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (type.HasFlag(BlockchainTransactionType.TokenApprove))
                return fa12.ApproveFee.ToTez();
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && isFirstTx)
                return fa12.ApproveFee.ToTez() * 2 + fa12.InitiateFee.ToTez() + (isRevealed ? 0 : fa12.RevealFee.ToTez());
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && !isFirstTx)
                return fa12.ApproveFee.ToTez() * 2 + fa12.AddFee.ToTez() + (isRevealed ? 0 : fa12.RevealFee.ToTez());
            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return fa12.RefundFee.ToTez() + (isRevealed ? 0 : fa12.RevealFee.ToTez());
            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return fa12.RedeemFee.ToTez() + (isRevealed ? 0 : fa12.RevealFee.ToTez());

            return fa12.TransferFee.ToTez() + (isRevealed ? 0 : fa12.RevealFee.ToTez());
        }

        private decimal ReserveFeeByTypeAsync(
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default)
        {
            var fa12 = Fa12;

            return Math.Max(fa12.RedeemFee.ToTez(), fa12.RefundFee.ToTez()) + fa12.RevealFee.ToTez() + Xtz.MicroTezReserve.ToTez();
        }

        private async Task<decimal> StorageFeeByTypeAsync(
            BlockchainTransactionType type,
            string to,
            bool isFirstTx,
            CancellationToken cancellationToken = default)
        {
            var fa12 = Fa12;

            var isActive = await IsAllocatedDestinationAsync(type, to, cancellationToken)
                .ConfigureAwait(false);

            if (type.HasFlag(BlockchainTransactionType.TokenApprove))
                return fa12.ApproveStorageLimit;
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && isFirstTx)
                return (fa12.ApproveStorageLimit + fa12.InitiateStorageLimit) / fa12.StorageFeeMultiplier;
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && !isFirstTx)
                return (fa12.ApproveStorageLimit + fa12.AddStorageLimit) / fa12.StorageFeeMultiplier;
            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return isActive
                    ? Math.Max((fa12.RefundStorageLimit - fa12.ActivationStorage) / fa12.StorageFeeMultiplier, 0) // without activation storage fee
                    : fa12.RefundStorageLimit / fa12.StorageFeeMultiplier;
            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return isActive
                    ? Math.Max((fa12.RedeemStorageLimit - fa12.ActivationStorage) / fa12.StorageFeeMultiplier, 0) // without activation storage fee
                    : fa12.RedeemStorageLimit / fa12.StorageFeeMultiplier;

            return isActive || !isFirstTx
                ? Math.Max((fa12.TransferStorageLimit - fa12.ActivationStorage) / fa12.StorageFeeMultiplier, 0) // without activation storage fee
                : fa12.TransferStorageLimit / fa12.StorageFeeMultiplier;
        }

        //private async Task<bool> IsAllocatedDestinationAsync(  // todo: check if "allocated" can be true with zero tez balance and non zero token balance
        //    BlockchainTransactionType type,
        //    string to,
        //    CancellationToken cancellationToken = default)
        //{
        //}

        #endregion Common

        #region Balances

        public override async Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            var fa12 = Fa12;

            var callingAddress = await GetMainXtzAddressAsync()
                .ConfigureAwait(false);

            if (callingAddress == null)
                return;

            using var callingAddressPublicKey = new SecureBytes((await GetAddressAsync(callingAddress.Address)
                .ConfigureAwait(false))
                .PublicKeyBytes());

            var txs = (await DataRepository
                .GetTransactionsAsync(Currency, fa12.TransactionType)
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
                        ? tx.Amount.FromTokenDigits(fa12.DigitsMultiplier)
                        : 0;

                    var outcome = isOutcome && !isFailed
                        ? -tx.Amount.FromTokenDigits(fa12.DigitsMultiplier)
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
                var fa12Api = fa12.BlockchainApi as ITokenBlockchainApi;

                var balanceResult = await fa12Api
                    .TryGetTokenBalanceAsync(
                        address: wa.Address,
                        callingAddress: callingAddress.Address,
                        securePublicKey: callingAddressPublicKey,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (balanceResult.HasError)
                {
                    Log.Error("Error while getting token balance for {@address} with code {@code} and description {@description}",
                        wa.Address,
                        balanceResult.Error.Code,
                        balanceResult.Error.Description);

                    continue; // todo: may be return?
                }

                wa.Balance = balanceResult.Value.FromTokenDigits(fa12.DigitsMultiplier);

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
            var fa12 = Fa12;

            var walletAddress = await DataRepository
                .GetWalletAddressAsync(Currency, address)
                .ConfigureAwait(false);

            if (walletAddress == null)
                return;

            var callingAddress = await GetMainXtzAddressAsync()  //todo make special function
                .ConfigureAwait(false);

            if (callingAddress == null)
                return;

            using var callingAddressPublicKey = new SecureBytes((await GetAddressAsync(callingAddress.Address)
                .ConfigureAwait(false))
                .PublicKeyBytes());

            var fa12Api = fa12.BlockchainApi as ITokenBlockchainApi;

            var balanceResult = await fa12Api
                .TryGetTokenBalanceAsync(
                    address: address,
                    callingAddress: callingAddress.Address,
                    securePublicKey: callingAddressPublicKey,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (balanceResult.HasError)
            {
                Log.Error("Error while balance update token for {@address} with code {@code} and description {@description}",
                    address,
                    balanceResult.Error.Code,
                    balanceResult.Error.Description);
                return;
            }

            var balance = balanceResult.Value.FromTokenDigits(fa12.DigitsMultiplier);

            // calculate unconfirmed balances
            var unconfirmedTxs = (await DataRepository
                .GetUnconfirmedTransactionsAsync(Currency, fa12.TransactionType)
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
                    ? utx.Amount.FromTokenDigits(fa12.DigitsMultiplier)
                    : 0;
                unconfirmedOutcome += address == utx.From && !isFailed
                    ? -utx.Amount.FromTokenDigits(fa12.DigitsMultiplier)
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

        private async Task<WalletAddress> GetMainXtzAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var xtzUnspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Xtz.Name)
                .ConfigureAwait(false))
                .ToList();

            xtzUnspentAddresses = xtzUnspentAddresses.SortList((a, b) => b.AvailableBalance().CompareTo(a.AvailableBalance()));

            return xtzUnspentAddresses.FirstOrDefault();
        }

        public override async Task<WalletAddress> GetRedeemAddressAsync(   //todo: match it with xtz balances
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return ResolvePublicKey(unspentAddresses.MaxBy(w => w.AvailableBalance()));

            var unspentTezosAddresses = await DataRepository
                .GetUnspentAddressesAsync("XTZ")
                .ConfigureAwait(false);

            if (unspentTezosAddresses.Any())
            {
                var tezosAddress = unspentTezosAddresses.MaxBy(a => a.AvailableBalance());

                return await DivideAddressAsync(
                    chain: tezosAddress.KeyIndex.Chain,
                    index: tezosAddress.KeyIndex.Index,
                    cancellationToken: cancellationToken);
            }

            foreach (var chain in new[] { Bip44.Internal, Bip44.External })
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
            var xtz = Xtz;

            if (addressUsagePolicy == AddressUsagePolicy.UseMinimalBalanceFirst)
            {
                from = from.ToList().SortList(new AvailableBalanceAscending());
            }
            else if (addressUsagePolicy == AddressUsagePolicy.UseMaximumBalanceFirst)
            {
                from = from.ToList().SortList(new AvailableBalanceDescending());
            }
            else if (addressUsagePolicy == AddressUsagePolicy.UseMaximumChainBalanceFirst)
            {
                var xtzUnspentAddresses = (await DataRepository
                    .GetUnspentAddressesAsync(xtz.Name)
                    .ConfigureAwait(false))
                    .ToList();

                xtzUnspentAddresses = xtzUnspentAddresses.SortList((a, b) => b.AvailableBalance().CompareTo(a.AvailableBalance()));

                from = xtzUnspentAddresses.FindAll(a => from.Select(b => b.Address).ToList().Contains(a.Address));
            }
            else if (addressUsagePolicy == AddressUsagePolicy.UseOnlyOneAddress)
            {
                var xtzUnspentAddresses = (await DataRepository
                    .GetUnspentAddressesAsync(xtz.Name)
                    .ConfigureAwait(false))
                    .ToList();

                xtzUnspentAddresses = xtzUnspentAddresses.SortList((a, b) => b.AvailableBalance().CompareTo(a.AvailableBalance()));

                from = from.ToList()
                    .Concat(xtzUnspentAddresses.FindAll(a => from.Select(b => b.Address).ToList().Contains(a.Address) == false))
                    .ToList();

                //"to" used for redeem and refund - using the destination address is prefered
                from = from.Where(a => a.Address == to).Concat(from.Where(a => a.Address != to)).ToList();
            }

            var result = new List<SelectedWalletAddress>();
            var requiredAmount = amount;

            var isFirstTx = true;
            var completed = false;

            foreach (var address in from)
            {
                var availableBalanceInTokens = address.AvailableBalance();

                var xtzAddress = await DataRepository
                    .GetWalletAddressAsync(xtz.Name, address.Address)
                    .ConfigureAwait(false);

                var availableBalanceInTez = xtzAddress.AvailableBalance();

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
                {
                    Log.Debug("Unsufficient XTZ ammount for FA12 token processing on address {@address} with available balance {@balance} and needed amount {@amount}",
                        xtzAddress.Address,
                        xtzAddress.AvailableBalance(),
                        txFeeInTez + storageFeeInTez + xtz.MicroTezReserve.ToTez());
                    continue;
                }

                if (addressUsagePolicy == AddressUsagePolicy.UseOnlyOneAddress)
                {
                    if (Math.Min(availableBalanceInTokens, requiredAmount) == requiredAmount)
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

                amountToUse = Math.Min(availableBalanceInTokens, requiredAmount);
                requiredAmount -= amountToUse;

                if (amountToUse > 0)
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
                        var estimatedFee = result.Sum(s => s.UsedFee);
                        var remainingFee = fee - estimatedFee;

                        var extraFee = 0m;

                        if (remainingFee > 0)
                        {
                            foreach (var s in result)
                            {
                                extraFee = s == result.Last()
                                    ? Math.Min(s.WalletAddress.AvailableBalance() - s.UsedFee - s.UsedStorageFee - xtz.MicroTezReserve.ToTez(), remainingFee)
                                    : Math.Min(s.WalletAddress.AvailableBalance() - s.UsedFee - s.UsedStorageFee - xtz.MicroTezReserve.ToTez(), Math.Round(remainingFee * s.UsedFee / estimatedFee, xtz.Digits));

                                remainingFee -= extraFee;
                                estimatedFee -= s.UsedFee;
                                s.UsedFee += extraFee;

                                if (remainingFee <= 0)
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

        public override async Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return unspentAddresses.MaxBy(a => a.AvailableBalance());

            var unspentTezosAddresses = await DataRepository
                .GetUnspentAddressesAsync("XTZ")
                .ConfigureAwait(false);

            if (unspentTezosAddresses.Any())
            {
                var tezosAddress = unspentTezosAddresses.MaxBy(a => a.AvailableBalance());

                return await DivideAddressAsync(
                    chain: tezosAddress.KeyIndex.Chain,
                    index: tezosAddress.KeyIndex.Index,
                    cancellationToken: cancellationToken);
            }

            return await DivideAddressAsync(
                    chain: Bip44.External,
                    index: 0,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion Addresses

        #region Helpers
        private JObject TransferParams(string from, string to, decimal amount)
        {
            return JObject.Parse(@"{'entrypoint':'transfer','value':{'prim':'Pair','args':[{'string':'" + from + "'},{'prim':'Pair','args':[{'string':'" + to + "'},{'int':'" + amount + "'}]}]}}");
        }

        #endregion Helpers
    }
}