using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.ERC20;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;

namespace Atomex.Wallet.Ethereum
{
    public class EthereumAccount : CurrencyAccount
    {
        public EthereumAccount(
            string currency,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository)
                : base(currency, currencies, wallet, dataRepository)
        {
        }

        #region Common

        private Atomex.Ethereum Eth => Currencies.Get<Atomex.Ethereum>(Currency);
        private EthereumTokens.ERC20 Erc20 => Currencies.Get<EthereumTokens.ERC20>("USDT");

        public override async Task<Error> SendAsync(
            IEnumerable<WalletAddress> from,
            string to,
            decimal amount,
            decimal feePerTx,
            decimal feePrice,
            bool useDefaultFee = false,
            CancellationToken cancellationToken = default)
        {
            var eth = Eth;

            var fromAddresses = from
                .Where(w => w.Address != to)
                .ToList();

            if (useDefaultFee)
                feePrice = await eth
                    .GetGasPriceAsync(cancellationToken)
                    .ConfigureAwait(false);

            var selectedAddresses = (await SelectUnspentAddressesAsync(
                    from: fromAddresses,
                    to: null,
                    amount: amount,
                    fee: feePerTx,
                    feePrice: feePrice,
                    feeUsagePolicy: useDefaultFee
                        ? FeeUsagePolicy.EstimatedFee
                        : FeeUsagePolicy.FeePerTransaction,
                    addressUsagePolicy: AddressUsagePolicy.UseMinimalBalanceFirst,
                    transactionType: BlockchainTransactionType.Output,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            if (!selectedAddresses.Any())
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: "Insufficient funds");

            if (feePerTx < eth.GasLimit)
                return new Error(
                    code: Errors.InsufficientGas,
                    description: "Insufficient gas");

            var feeAmount = eth.GetFeeAmount(feePerTx, feePrice);

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
                    .GetNonceAsync(eth, selectedAddress.WalletAddress.Address)
                    .ConfigureAwait(false);

                if (nonceAsyncResult.HasError)
                    return nonceAsyncResult.Error;

                var tx = new EthereumTransaction
                {
                    Currency = eth,
                    Type = BlockchainTransactionType.Output,
                    CreationTime = DateTime.UtcNow,
                    To = to.ToLowerInvariant(),
                    Amount = Atomex.Ethereum.EthToWei(selectedAddress.UsedAmount),
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

                var broadcastResult = await eth.BlockchainApi
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

                await UpsertTransactionAsync(
                        tx: tx,
                        updateBalance: false,
                        notifyIfUnconfirmed: true,
                        notifyIfBalanceUpdated: false,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            _ = UpdateBalanceAsync(cancellationToken);

            return null;
        }

        public override async Task<Error> SendAsync(
            string to,
            decimal amount,
            decimal feePerTx,
            decimal feePrice,
            bool useDefaultFee = false,
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
                    feePerTx: feePerTx,
                    feePrice: feePrice,
                    useDefaultFee: useDefaultFee,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<decimal?> EstimateFeeAsync(
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
                    to: null,
                    amount: amount,
                    fee: fee,
                    feePrice: feePrice == 0
                        ? await Eth.GetGasPriceAsync(cancellationToken).ConfigureAwait(false)
                        : feePrice,
                    feeUsagePolicy: fee == 0 ? FeeUsagePolicy.EstimatedFee : FeeUsagePolicy.FeePerTransaction,
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
            decimal feePerTx = 0,
            decimal feePrice = 0,
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            var eth = Eth;

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

            var gasPrice = await eth
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var reserveFeeInEth = ReserveFee(gasPrice);

            foreach (var address in unspentAddresses)
            {
                var feeInEth = eth.GetFeeAmount(
                    feePerTx == 0
                        ? GasLimitByType(type, isFirstTx)
                        : feePerTx,
                    feePrice == 0
                        ? gasPrice
                        : feePrice);

                var usedAmountInEth = Math.Max(address.AvailableBalance() - feeInEth - (reserve && address == unspentAddresses.Last() ? reserveFeeInEth : 0), 0);

                if (usedAmountInEth <= 0)
                    continue;

                amount += usedAmountInEth;
                fee += feeInEth;

                if (isFirstTx)
                    isFirstTx = false;
            }

            return (amount, fee, 0m);
        }

        private decimal GasLimitByType(BlockchainTransactionType type, bool isFirstTx)
        {
            var eth = Eth;

            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && isFirstTx)
                return eth.InitiateWithRewardGasLimit;
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && !isFirstTx)
                return eth.AddGasLimit;
            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return eth.RefundGasLimit;
            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return eth.RedeemGasLimit;

            return eth.GasLimit;
        }

        private decimal ReserveFee(decimal gasPrice)
        {
            var eth = Eth;
            var erc20 = Erc20;

            return Math.Max(
                eth.GetFeeAmount(Math.Max(erc20.RefundGasLimit, erc20.RedeemGasLimit), gasPrice),
                eth.GetFeeAmount(Math.Max(eth.RefundGasLimit, eth.RedeemGasLimit), gasPrice));
        }

        protected override async Task<bool> ResolveTransactionTypeAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var eth = Eth;

            if (!(tx is EthereumTransaction ethTx))
                throw new ArgumentException("Invalid tx type", nameof(tx));

            var oldTx = !ethTx.IsInternal
                ? await DataRepository
                    .GetTransactionByIdAsync(Currency, tx.Id, Eth.TransactionType)
                    .ConfigureAwait(false)
                : null;

            if (oldTx != null && oldTx.IsConfirmed)
                return false;

            var isFromSelf = await IsSelfAddressAsync(
                    address: ethTx.From,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (isFromSelf)
            {
                ethTx.Type |= BlockchainTransactionType.Output;

                var isToSwapContract = ethTx.To == eth.SwapContractAddress;

                if (isToSwapContract)
                {
                    // todo: recognize swap payment/refund/redeem
                }
                else if (ethTx.Amount == 0)
                {
                    ethTx = ethTx.ParseERC20TransactionType();
                }
            }

            var isToSelf = await IsSelfAddressAsync(
                    address: ethTx.To,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (isToSelf)
                ethTx.Type |= BlockchainTransactionType.Input;

            if (oldTx != null)
                ethTx.Type |= oldTx.Type;

            ethTx.InternalTxs?.ForEach(async t => await ResolveTransactionTypeAsync(t, cancellationToken)
                .ConfigureAwait(false));

            return true;
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
                    var eth = Eth;

                    var txs = (await DataRepository
                        .GetTransactionsAsync(Currency, eth.TransactionType)
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

                    var totalUnconfirmedIncome = 0m;
                    var totalUnconfirmedOutcome = 0m;
                    var addressBalances = new Dictionary<string, WalletAddress>();

                    foreach (var tx in txs.Concat(internalTxs))
                    {
                        var addresses = new HashSet<string>();

                        var isFromSelf = await IsSelfAddressAsync(tx.From, cancellationToken)
                            .ConfigureAwait(false);

                        if (isFromSelf)
                            addresses.Add(tx.From);

                        var isToSelf = await IsSelfAddressAsync(tx.To, cancellationToken)
                            .ConfigureAwait(false);

                        if (isToSelf)
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
                                //walletAddress.Balance            += isConfirmed ? income + outcome : 0;
                                walletAddress.UnconfirmedIncome += !isConfirmed ? income : 0;
                                walletAddress.UnconfirmedOutcome += !isConfirmed ? outcome : 0;
                            }
                            else
                            {
                                walletAddress = await DataRepository
                                    .GetWalletAddressAsync(Currency, address)
                                    .ConfigureAwait(false);

                                //walletAddress.Balance            = isConfirmed ? income + outcome : 0;
                                walletAddress.UnconfirmedIncome = !isConfirmed ? income : 0;
                                walletAddress.UnconfirmedOutcome = !isConfirmed ? outcome : 0;
                                walletAddress.HasActivity = true;

                                addressBalances.Add(address, walletAddress);
                            }

                            //totalBalance            += isConfirmed ? income + outcome : 0;
                            totalUnconfirmedIncome += !isConfirmed ? income : 0;
                            totalUnconfirmedOutcome += !isConfirmed ? outcome : 0;
                        }
                    }

                    var totalBalance = 0m;
                    var api = eth.BlockchainApi;

                    foreach (var wa in addressBalances.Values)
                    {
                        var balanceResult = await api
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

                        wa.Balance = balanceResult.Value;

                        totalBalance += wa.Balance;
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
                catch (OperationCanceledException)
                {
                    Log.Debug($"{Currency} UpdateBalanceAsync canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, $"{Currency} UpdateBalanceAsync error.");
                }

            }, cancellationToken);
        }

        public override async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var eth = Eth;

            var walletAddress = await DataRepository
                .GetWalletAddressAsync(Currency, address)
                .ConfigureAwait(false);

            if (walletAddress == null)
                return;

            var balanceResult = await eth.BlockchainApi
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

            var balance = balanceResult.Value;

            // calculate unconfirmed balances
            var unconfirmedTxs = (await DataRepository
                .GetUnconfirmedTransactionsAsync(Currency, eth.TransactionType)
                .ConfigureAwait(false))
                .Cast<EthereumTransaction>()
                .ToList();

            var unconfirmedInternalTxs = unconfirmedTxs.Aggregate(new List<EthereumTransaction>(), (list, tx) =>
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
                    ? Atomex.Ethereum.WeiToEth(utx.Amount)
                    : 0;
                unconfirmedOutcome += address == utx.From && !isFailed
                    ? -Atomex.Ethereum.WeiToEth(utx.Amount + utx.GasPrice * (utx.GasUsed != 0 ? utx.GasUsed : utx.GasLimit))
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

        public override async Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return ResolvePublicKey(unspentAddresses.MaxBy(w => w.AvailableBalance()));

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

        public override async Task<IEnumerable<WalletAddress>> GetUnspentTokenAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            if (Currency == "ETH")
                return await DataRepository
                    .GetUnspentAddressesAsync(Currency)
                    .ConfigureAwait(false);

            // todo: refactoring
            var usdtAddresses = await DataRepository
                .GetUnspentAddressesAsync("USDT")
                .ConfigureAwait(false);

            var tbtcAddresses = await DataRepository
                .GetUnspentAddressesAsync("TBTC")
                .ConfigureAwait(false);

            var wbtcAddresses = await DataRepository
                .GetUnspentAddressesAsync("WBTC")
                .ConfigureAwait(false);

            return usdtAddresses.Concat(tbtcAddresses).Concat(wbtcAddresses);
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
                    to: null,
                    amount: amount,
                    fee: fee,
                    feePrice: feePrice,
                    feeUsagePolicy: feeUsagePolicy,
                    addressUsagePolicy: addressUsagePolicy,
                    transactionType: transactionType,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return ResolvePublicKeys(selectedAddresses
                .Select(w => w.WalletAddress)
                .ToList());
        }

        public override Task<IEnumerable<SelectedWalletAddress>> SelectUnspentAddressesAsync(
            IList<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default)
        {
            var eth = Eth;

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
                var feeInEth = feeUsagePolicy == FeeUsagePolicy.EstimatedFee
                    ? eth.GetFeeAmount(GasLimitByType(transactionType, isFirstTx: true), feePrice)
                    : eth.GetFeeAmount(fee, feePrice);

                var address = from.FirstOrDefault(w => w.AvailableBalance() >= amount + feeInEth);

                var selectedAddresses = address != null
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

                return Task.FromResult(selectedAddresses);
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
                        ? eth.GetFeeAmount(GasLimitByType(transactionType, isFirstTx), feePrice)
                        : feeUsagePolicy == FeeUsagePolicy.FeeForAllTransactions
                            ? Math.Round(eth.GetFeeAmount(fee, feePrice) / txCount, eth.Digits)
                            : eth.GetFeeAmount(fee, feePrice);

                    if (availableBalance <= txFee) // ignore address with balance less than fee
                    {
                        if (result.Count + from.Count - from.IndexOf(address) <= txCount)
                            break;
                        else
                            continue;
                    }

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
                    return Task.FromResult<IEnumerable<SelectedWalletAddress>>(result);
            }

            return Task.FromResult(Enumerable.Empty<SelectedWalletAddress>());
        }

        #endregion Addresses
    }
}