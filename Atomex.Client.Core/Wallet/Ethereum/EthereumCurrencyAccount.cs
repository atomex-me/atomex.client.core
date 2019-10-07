using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Atomex.Wallet.Abstract;
using Serilog;

namespace Atomex.Wallet.Ethereum
{
    public class EthereumCurrencyAccount : CurrencyAccount
    {
        public EthereumCurrencyAccount(
            Currency currency,
            IHdWallet wallet,
            IAccountDataRepository dataRepository)
                : base(currency, wallet, dataRepository)
        {
        }

        #region Common

        private Atomex.Ethereum Eth => (Atomex.Ethereum) Currency;

        public override async Task<Error> SendAsync(
            IEnumerable<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var selectedAddresses = SelectUnspentAddresses(
                    from: from.ToList(),
                    amount: amount,
                    fee: fee,
                    feePrice: feePrice,
                    feeUsagePolicy: FeeUsagePolicy.FeeForAllTransactions,
                    addressUsagePolicy: AddressUsagePolicy.UseMinimalBalanceFirst,
                    transactionType: BlockchainTransactionType.Output)
                .ToList();

            if (!selectedAddresses.Any())
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: "Insufficient funds");

            var feePerTx = Math.Round(fee / selectedAddresses.Count);

            if (feePerTx < Eth.GasLimit)
                return new Error(
                    code: Errors.InsufficientGas,
                    description: "Insufficient gas");

            var feeAmount = Eth.GetFeeAmount(feePerTx, feePrice);

            Log.Debug("Fee per transaction {@feePerTransaction}. Fee Amount {@feeAmount}",
                feePerTx,
                feeAmount);

            foreach (var selectedAddress in selectedAddresses)
            {
                Log.Debug("Send {@amount} ETH from address {@address} with available balance {@balance}",
                    selectedAddress.UsedAmount,
                    selectedAddress.WalletAddress.Address,
                    selectedAddress.WalletAddress.AvailableBalance());

                var nonceAsyncResult = await EthereumNonceManager.Instance
                    .GetNonceAsync(Eth, selectedAddress.WalletAddress.Address)
                    .ConfigureAwait(false);

                if (nonceAsyncResult.HasError)
                    return nonceAsyncResult.Error;

                var tx = new EthereumTransaction
                {
                    Currency = Eth,
                    Type = BlockchainTransactionType.Output,
                    CreationTime = DateTime.UtcNow,
                    To = to.ToLowerInvariant(),
                    Amount = new BigInteger(Atomex.Ethereum.EthToWei(selectedAddress.UsedAmount)),
                    Nonce = nonceAsyncResult.Value,
                    GasPrice = new BigInteger(Atomex.Ethereum.GweiToWei(feePrice)),
                    GasLimit = new BigInteger(feePerTx),
                };

                var signResult = await Wallet
                    .SignAsync(tx, selectedAddress.WalletAddress, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                    return new Error(
                        code: Errors.TransactionSigningError,
                        description: "Transaction signing error");

                if (!tx.Verify())
                    return new Error(
                        code: Errors.TransactionVerificationError,
                        description: "Transaction verification error");

                var asyncResult = await Currency.BlockchainApi
                    .BroadcastAsync(tx, cancellationToken)
                    .ConfigureAwait(false);

                if (asyncResult.HasError)
                    return asyncResult.Error;

                var txId = asyncResult.Value;

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
            CancellationToken cancellationToken = default(CancellationToken))
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
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (!unspentAddresses.Any())
                return null; // insufficient funds

            var selectedAddresses = SelectUnspentAddresses(
                    from: unspentAddresses,
                    amount: amount,
                    fee: 0,
                    feePrice: Eth.GasPriceInGwei,
                    feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                    addressUsagePolicy: AddressUsagePolicy.UseMinimalBalanceFirst,
                    transactionType: type)
                .ToList();

            if (!selectedAddresses.Any())
                return null; // insufficient funds

            return selectedAddresses.Sum(s => s.UsedFee);
        }

        public override async Task<(decimal, decimal)> EstimateMaxAmountToSendAsync(
            string to,
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (!unspentAddresses.Any())
                return (0m, 0m);

            var isFirstTx = true;
            var amount = 0m;
            var fee = 0m;

            foreach (var address in unspentAddresses)
            {
                var feeInEth = Eth.GetFeeAmount(GasLimitByType(type, isFirstTx), Eth.GasPriceInGwei);
                var usedAmountInEth = Math.Max(address.AvailableBalance() - feeInEth, 0);

                if (usedAmountInEth <= 0)
                    continue;

                amount += usedAmountInEth;
                fee += feeInEth;

                if (isFirstTx)
                    isFirstTx = false;
            }

            return (amount, fee);
        }

        private decimal GasLimitByType(BlockchainTransactionType type, bool isFirstTx)
        {
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && isFirstTx)
                return Eth.InitiateWithRewardGasLimit;
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && !isFirstTx)
                return Eth.AddGasLimit;
            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return Eth.RefundGasLimit;
            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return Eth.RedeemGasLimit;

            return Eth.GasLimit;
        }

        protected override async Task ResolveTransactionTypeAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!(tx is EthereumTransaction ethTx))
                throw new ArgumentException("Invalid tx type", nameof(tx));

            var isFromSelf = await IsSelfAddressAsync(
                    address: ethTx.From,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (isFromSelf)
                ethTx.Type |= BlockchainTransactionType.Output;

            var isToSelf = await IsSelfAddressAsync(
                    address: ethTx.To,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (isToSelf)
                ethTx.Type |= BlockchainTransactionType.Input;

            // todo: recognize swap payment/refund/redeem

            var oldTx = !ethTx.IsInternal
                ? await DataRepository
                    .GetTransactionByIdAsync(Currency, tx.Id)
                    .ConfigureAwait(false)
                : null;

            if (oldTx != null)
                ethTx.Type |= oldTx.Type;

            ethTx.InternalTxs?.ForEach(async t => await ResolveTransactionTypeAsync(t, cancellationToken)
                .ConfigureAwait(false));
        }

        #endregion Common

        #region Balances

        public override async Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var txs = (await DataRepository
                .GetTransactionsAsync(Currency)
                .ConfigureAwait(false))
                .Cast<EthereumTransaction>()
                .ToList();

            var internalTxs = txs.Aggregate(new List<EthereumTransaction>(), (list, tx) =>
            {
                if (tx.InternalTxs != null)
                    list.AddRange(tx.InternalTxs);

                return list;
            });

            // calculate balances
            var totalBalance = 0m;
            var totalUnconfirmedIncome = 0m;
            var totalUnconfirmedOutcome = 0m;
            var addressBalances = new Dictionary<string, WalletAddress>();

            foreach (var tx in txs.Concat(internalTxs))
            {
                var addresses = new HashSet<string>();

                if (tx.Type.HasFlag(BlockchainTransactionType.Output))
                    addresses.Add(tx.From);

                if (tx.Type.HasFlag(BlockchainTransactionType.Input))
                    addresses.Add(tx.To);

                foreach (var address in addresses)
                {
                    var isIncome = address == tx.To;
                    var isOutcome = address == tx.From;
                    var isConfirmed = tx.IsConfirmed;
                    var isFailed = tx.State == BlockchainTransactionState.Failed;

                    var income = isIncome && !isFailed
                        ? Atomex.Ethereum.WeiToEth(tx.Amount)
                        : 0;

                    var outcome = isOutcome
                        ? (!isFailed 
                            ? -Atomex.Ethereum.WeiToEth(tx.Amount + tx.GasPrice * (tx.GasUsed != 0 ? tx.GasUsed : tx.GasLimit))
                            : -Atomex.Ethereum.WeiToEth(tx.GasPrice * tx.GasUsed))
                        : 0;
    
                    if (addressBalances.TryGetValue(address, out var walletAddress))
                    {
                        walletAddress.Balance            += isConfirmed ? income + outcome : 0;
                        walletAddress.UnconfirmedIncome  += !isConfirmed ? income : 0;
                        walletAddress.UnconfirmedOutcome += !isConfirmed ? outcome : 0;
                    }
                    else
                    {
                        walletAddress = await DataRepository
                            .GetWalletAddressAsync(Currency, address)
                            .ConfigureAwait(false);

                        walletAddress.Balance            = isConfirmed ? income + outcome : 0;
                        walletAddress.UnconfirmedIncome  = !isConfirmed ? income : 0;
                        walletAddress.UnconfirmedOutcome = !isConfirmed ? outcome : 0;
                        walletAddress.HasActivity = true;

                        addressBalances.Add(address, walletAddress);
                    }

                    totalBalance            += isConfirmed ? income + outcome : 0;
                    totalUnconfirmedIncome  += !isConfirmed ? income : 0;
                    totalUnconfirmedOutcome += !isConfirmed ? outcome : 0;
                }
            }

            // upsert addresses
            await DataRepository
                .UpsertAddressesAsync(addressBalances.Values)
                .ConfigureAwait(false);

            Balance = totalBalance;
            UnconfirmedIncome = totalUnconfirmedIncome;
            UnconfirmedOutcome = totalUnconfirmedOutcome;

            RaiseBalanceUpdated(new CurrencyEventArgs(Currency));
        }

        public override async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var txs = (await DataRepository
                .GetTransactionsAsync(Currency)
                .ConfigureAwait(false))
                .Cast<EthereumTransaction>()
                .ToList();

            var internalTxs = txs.Aggregate(new List<EthereumTransaction>(), (list, tx) =>
            {
                if (tx.InternalTxs != null)
                    list.AddRange(tx.InternalTxs);

                return list;
            });

            var walletAddress = await DataRepository
                .GetWalletAddressAsync(Currency, address)
                .ConfigureAwait(false);

            var balance = 0m;
            var unconfirmedIncome = 0m;
            var unconfirmedOutcome = 0m;

            foreach (var tx in txs.Concat(internalTxs))
            {
                var isIncome = address == tx.To;
                var isOutcome = address == tx.From;
                var isConfirmed = tx.IsConfirmed;
                var isFailed = tx.State == BlockchainTransactionState.Failed;

                var income = isIncome && !isFailed
                    ? Atomex.Ethereum.WeiToEth(tx.Amount)
                    : 0;

                var outcome = isOutcome
                    ? (!isFailed
                        ? -Atomex.Ethereum.WeiToEth(tx.Amount + tx.GasPrice * (tx.GasUsed != 0 ? tx.GasUsed : tx.GasLimit))
                        : -Atomex.Ethereum.WeiToEth(tx.GasPrice * tx.GasUsed))
                    : 0;

                balance            += isConfirmed ? income + outcome : 0;
                unconfirmedIncome  += !isConfirmed ? income : 0;
                unconfirmedOutcome += !isConfirmed ? outcome : 0;
            }

            var balanceDifference            = balance - walletAddress.Balance;
            var unconfirmedIncomeDifference  = unconfirmedIncome - walletAddress.UnconfirmedIncome;
            var unconfirmedOutcomeDifference = unconfirmedOutcome - walletAddress.UnconfirmedOutcome;

            if (balanceDifference != 0 ||
                unconfirmedIncomeDifference != 0 ||
                unconfirmedOutcomeDifference != 0)
            {
                walletAddress.Balance            = balance;
                walletAddress.UnconfirmedIncome  = unconfirmedIncome;
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

        public override async Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            decimal amount,
            decimal fee,
            decimal feePrice,
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            var selectedAddresses = SelectUnspentAddresses(
                from: unspentAddresses,
                amount: amount,
                fee: fee,
                feePrice: feePrice,
                feeUsagePolicy: feeUsagePolicy,
                addressUsagePolicy: addressUsagePolicy,
                transactionType: transactionType);

            return ResolvePublicKeys(selectedAddresses
                .Select(w => w.WalletAddress)
                .ToList());
        }

        private IEnumerable<SelectedWalletAddress> SelectUnspentAddresses(
            IList<WalletAddress> from,
            decimal amount,
            decimal fee,
            decimal feePrice,
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType)
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
                var feeInEth = feeUsagePolicy == FeeUsagePolicy.EstimatedFee
                    ? Eth.GetFeeAmount(GasLimitByType(transactionType, isFirstTx: true), Eth.GasPriceInGwei)
                    : Eth.GetFeeAmount(fee, feePrice);

                var address = from.FirstOrDefault(w => w.AvailableBalance() >= amount + feeInEth);

                return address != null
                    ? new List<SelectedWalletAddress>
                    {
                        new SelectedWalletAddress
                        {
                            WalletAddress = address,
                            UsedAmount = amount,
                            UsedFee = feeInEth
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
                    var availableBalance = address.AvailableBalance();

                    var txFee = feeUsagePolicy == FeeUsagePolicy.EstimatedFee
                        ? Eth.GetFeeAmount(GasLimitByType(transactionType, isFirstTx), Eth.GasPriceInGwei)
                        : feeUsagePolicy == FeeUsagePolicy.FeeForAllTransactions
                            ? Math.Round(Eth.GetFeeAmount(fee, feePrice) / txCount, Eth.Digits)
                            : Eth.GetFeeAmount(fee, feePrice);

                    if (availableBalance <= txFee) // ignore address with balance less than fee
                        continue;

                    var amountToUse = Math.Min(Math.Max(availableBalance - txFee, 0), requiredAmount);

                    result.Add(new SelectedWalletAddress
                    {
                        WalletAddress = address,
                        UsedAmount = amountToUse,
                        UsedFee = txFee
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