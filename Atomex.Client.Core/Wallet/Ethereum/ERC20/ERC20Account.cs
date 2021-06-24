using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Nethereum.RPC.Eth.DTOs;
using Nethereum.Contracts;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.ERC20;
using Atomex.Common;
using Atomex.Core;
using Atomex.EthereumTokens;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;

namespace Atomex.Wallet.Ethereum
{
    public class Erc20Account : CurrencyAccount, ILegacyCurrencyAccount
    {
        public Erc20Account(
            string currency,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository)
                : base(currency, currencies, wallet, dataRepository)
        {
        }

        #region Common

        private Erc20Config Erc20Config => Currencies.Get<Erc20Config>(Currency);
        private EthereumConfig EthConfig => Currencies.Get<EthereumConfig>("ETH");

        public async Task<Error> SendAsync(
            IEnumerable<WalletAddress> from,
            string to,
            decimal amount,
            decimal feePerTx = 0,
            decimal feePrice = 0,
            bool useDefaultFee = false,
            CancellationToken cancellationToken = default)
        {
            var erc20Config = Erc20Config;

            var fromAddresses = from
                .Where(w => w.Address != to) // filter self address usage
                .ToList();

            var selectedAddresses = (await SelectUnspentAddresses(
                    from: fromAddresses,
                    amount: amount,
                    fee: feePerTx,
                    feePrice: feePrice,
                    feeUsagePolicy: useDefaultFee ? FeeUsagePolicy.EstimatedFee : FeeUsagePolicy.FeePerTransaction,
                    addressUsagePolicy: AddressUsagePolicy.UseMinimalBalanceFirst,
                    transactionType: BlockchainTransactionType.Output)
                .ConfigureAwait(false))
                .ToList();

            if (!selectedAddresses.Any())
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: "Insufficient funds");

            if (feePerTx < erc20Config.TransferGasLimit)
                return new Error(
                    code: Errors.InsufficientGas,
                    description: "Insufficient gas");

            var feeAmount = erc20Config.GetFeeAmount(feePerTx, feePrice);

            Log.Debug("Fee per transaction {@feePerTransaction}. Fee Amount {@feeAmount}",
                feePerTx,
                feeAmount);

            foreach (var selectedAddress in selectedAddresses)
            {
                Log.Debug("Send {@amount} of {@currency} from address {@address} with available balance {@balance}",
                    selectedAddress.UsedAmount,
                    erc20Config.Name,
                    selectedAddress.WalletAddress.Address,
                    selectedAddress.WalletAddress.AvailableBalance());

                using var addressLock = await EthereumAccount.AddressLocker
                    .GetLockAsync(selectedAddress.WalletAddress.Address, cancellationToken)
                    .ConfigureAwait(false);

                var nonceResult = await EthereumNonceManager.Instance
                    .GetNonceAsync(EthConfig, selectedAddress.WalletAddress.Address)
                    .ConfigureAwait(false);

                if (nonceResult.HasError)
                    return nonceResult.Error;

                TransactionInput txInput;

                var message = new ERC20TransferFunctionMessage
                {
                    To          = to.ToLowerInvariant(),
                    Value       = erc20Config.TokensToTokenDigits(selectedAddress.UsedAmount),
                    FromAddress = selectedAddress.WalletAddress.Address,
                    Gas         = new BigInteger(feePerTx),
                    GasPrice    = new BigInteger(EthereumConfig.GweiToWei(feePrice)),
                    Nonce       = nonceResult.Value
                };

                txInput = message.CreateTransactionInput(erc20Config.ERC20ContractAddress);

                var tx = new EthereumTransaction(erc20Config.Name, txInput)
                {
                    Type = BlockchainTransactionType.Output
                };

                var signResult = await Wallet
                    .SignAsync(tx, selectedAddress.WalletAddress, erc20Config, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                    return new Error(
                        code: Errors.TransactionSigningError,
                        description: "Transaction signing error");

                if (!tx.Verify(erc20Config))
                    return new Error(
                        code: Errors.TransactionVerificationError,
                        description: "Transaction verification error");

                var broadcastResult = await erc20Config.BlockchainApi
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

                tx.Amount = erc20Config.TokensToTokenDigits(selectedAddress.UsedAmount);
                tx.To = to.ToLowerInvariant();

                await UpsertTransactionAsync(
                        tx: tx,
                        updateBalance: false,
                        notifyIfUnconfirmed: true,
                        notifyIfBalanceUpdated: false,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var ethTx = tx.Clone();
                ethTx.Currency = EthConfig.Name;
                ethTx.Amount = 0;
                ethTx.Type = BlockchainTransactionType.TokenCall;

                await UpsertTransactionAsync(
                        tx: ethTx,
                        updateBalance: false,
                        notifyIfUnconfirmed: true,
                        notifyIfBalanceUpdated: false,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            _ = UpdateBalanceAsync(cancellationToken);

            return null;
        }

        public async Task<Error> SendAsync(
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

        public async Task<decimal?> EstimateFeeAsync(
            string to,
            decimal amount,
            BlockchainTransactionType type,
            decimal fee = 0,
            decimal feePrice = 0,
            CancellationToken cancellationToken = default)
        {
            var erc20 = Erc20Config;

            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (!unspentAddresses.Any())
                return null; // insufficient funds

            var selectedAddresses = (await SelectUnspentAddresses(
                    from: unspentAddresses,
                    amount: amount,
                    fee: fee,
                    feePrice: feePrice == 0
                        ? await erc20
                            .GetGasPriceAsync(cancellationToken)
                            .ConfigureAwait(false)
                        : feePrice,
                    feeUsagePolicy: fee == 0 ? FeeUsagePolicy.EstimatedFee : FeeUsagePolicy.FeePerTransaction,
                    addressUsagePolicy: AddressUsagePolicy.UseMinimalBalanceFirst,
                    transactionType: type)
                .ConfigureAwait(false))
                .ToList();

            if (!selectedAddresses.Any())
                return null; // insufficient funds

            return selectedAddresses.Sum(s => s.UsedFee);
        }

        public async Task<(decimal, decimal, decimal)> EstimateMaxAmountToSendAsync(
            string to,
            BlockchainTransactionType type,
            decimal feePerTx = 0,
            decimal feePrice = 0,
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            var eth = EthConfig;

            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (!unspentAddresses.Any())
                return (0m, 0m, 0m);

            // minimum balance first
            unspentAddresses = unspentAddresses
                .ToList()
                .SortList(new AvailableBalanceAscending());

            var isFirstTx = true;
            var amount = 0m;
            var fee = 0m;

            var gasPrice = await eth.GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var reserveFeeInEth = ReserveFee(gasPrice);

            foreach (var address in unspentAddresses)
            {
                var ethAddress = await DataRepository
                    .GetWalletAddressAsync(eth.Name, address.Address)
                    .ConfigureAwait(false);

                if (ethAddress == null)
                    continue;

                var ethAvailableBalance = ethAddress.AvailableBalance();

                var feeInEth = eth.GetFeeAmount(
                    feePerTx == 0
                        ? GasLimitByType(type, isFirstTx)
                        : feePerTx,
                    feePrice == 0
                        ? gasPrice
                        : feePrice);

                if (ethAddress.AvailableBalance() - feeInEth - (reserve && address == unspentAddresses.Last() ? reserveFeeInEth : 0) < 0)
                    continue;

                amount += address.AvailableBalance();
                fee += feeInEth;

                if (isFirstTx)
                    isFirstTx = false;
            }

            return (amount, fee, 0m);
        }

        public async Task<decimal> EstimateMaxFeeAsync(
            string to,
            decimal amount,
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default)
        {
            var eth = EthConfig;

            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (!unspentAddresses.Any())
                return 0m; // insufficient funds

            var selectedAddresses = (await SelectUnspentAddresses(
                    from: unspentAddresses,
                    amount: amount,
                    fee: 0,
                    feePrice: await eth
                        .GetGasPriceAsync(cancellationToken)
                        .ConfigureAwait(false),
                    feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                    addressUsagePolicy: AddressUsagePolicy.UseMaximumChainBalanceFirst,  //todo: calc efficiency for UseMaximumBalanceFirst
                    transactionType: type)
                .ConfigureAwait(false))
                .ToList();

            if (!selectedAddresses.Any())
                return 0m; // insufficient funds

            decimal maxTxFee = 0m;

            foreach (var addr in selectedAddresses)
            {
                var ethAddress = await DataRepository
                    .GetWalletAddressAsync(eth.Name, addr.WalletAddress.Address)
                    .ConfigureAwait(false);

                maxTxFee = maxTxFee > 0 ? Math.Min(maxTxFee, ethAddress.AvailableBalance()) : ethAddress.AvailableBalance();
            }

            return maxTxFee * selectedAddresses.Count();
        }

        private decimal GasLimitByType(BlockchainTransactionType type, bool isFirstTx)
        {
            var erc20 = Erc20Config;

            if (type.HasFlag(BlockchainTransactionType.TokenApprove))
                return erc20.ApproveGasLimit;
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && isFirstTx) // todo: recheck
                return erc20.ApproveGasLimit * 2 + erc20.InitiateWithRewardGasLimit;
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && !isFirstTx) // todo: recheck
                return erc20.ApproveGasLimit * 2 + erc20.AddGasLimit;
            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return erc20.RefundGasLimit;
            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return erc20.RedeemGasLimit;

            return erc20.TransferGasLimit;
        }

        private decimal ReserveFee(decimal gasPrice)
        {
            var eth = EthConfig;
            var erc20 = Erc20Config;

            return Math.Max(
                eth.GetFeeAmount(Math.Max(erc20.RefundGasLimit, erc20.RedeemGasLimit), gasPrice),
                eth.GetFeeAmount(Math.Max(eth.RefundGasLimit, eth.RedeemGasLimit), gasPrice));
        }

        protected override async Task<bool> ResolveTransactionTypeAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var erc20 = Erc20Config;

            if (!(tx is EthereumTransaction ethTx))
                throw new ArgumentException("Invalid tx type", nameof(tx));

            var oldTx = (EthereumTransaction) await DataRepository
                .GetTransactionByIdAsync(Currency, tx.Id, erc20.TransactionType)
                .ConfigureAwait(false);

            //if (oldTx != null && oldTx.IsConfirmed)
            //    return false;

            var isFromSelf = await IsSelfAddressAsync(
                    address: ethTx.From,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (isFromSelf && ethTx.Currency == erc20.Name)
                ethTx.Type |= BlockchainTransactionType.Output;

            var isToSelf = await IsSelfAddressAsync(
                    address: ethTx.To,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (isToSelf && ethTx.Currency == erc20.Name)
                ethTx.Type |= BlockchainTransactionType.Input;

            // todo: recognize swap payment/refund/redeem

            if (oldTx != null)
            {
                if (ethTx.IsInternal)
                {
                    if (oldTx.Type.HasFlag(BlockchainTransactionType.SwapPayment))
                        ethTx.Type |= BlockchainTransactionType.SwapPayment;
                    if (oldTx.Type.HasFlag(BlockchainTransactionType.SwapRedeem))
                        ethTx.Type |= BlockchainTransactionType.SwapRedeem;
                    if (oldTx.Type.HasFlag(BlockchainTransactionType.SwapRefund))
                        ethTx.Type |= BlockchainTransactionType.SwapRefund;
                }
                else
                {
                    ethTx.Type |= oldTx.Type;
                }
            }

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
                    var erc20 = Erc20Config;

                    var txs = (await DataRepository
                        .GetTransactionsAsync(Currency, erc20.TransactionType)
                        .ConfigureAwait(false))
                        .Cast<EthereumTransaction>()
                        .ToList();

                    // calculate balances
                    var totalBalance = 0m;
                    var totalUnconfirmedIncome = 0m;
                    var totalUnconfirmedOutcome = 0m;
                    var addressBalances = new Dictionary<string, WalletAddress>();

                    foreach (var tx in txs)
                    {
                        try
                        {
                            var addresses = new HashSet<string>();

                            if (tx.Type.HasFlag(BlockchainTransactionType.Input))
                                addresses.Add(tx.To);

                            if (tx.Type.HasFlag(BlockchainTransactionType.Output))
                                addresses.Add(tx.From);

                            foreach (var address in addresses)
                            {
                                var isIncome = address == tx.To;
                                var isOutcome = address == tx.From;
                                var isConfirmed = tx.IsConfirmed;
                                var isFailed = tx.State == BlockchainTransactionState.Failed;

                                var income = isIncome && !isFailed
                                    ? erc20.TokenDigitsToTokens(tx.Amount)
                                    : 0;

                                var outcome = isOutcome && !isFailed
                                    ? -erc20.TokenDigitsToTokens(tx.Amount)
                                    : 0;

                                if (addressBalances.TryGetValue(address, out var walletAddress))
                                {
                                    walletAddress.Balance += isConfirmed ? income + outcome : 0;
                                    walletAddress.UnconfirmedIncome += !isConfirmed ? income : 0;
                                    walletAddress.UnconfirmedOutcome += !isConfirmed ? outcome : 0;
                                }
                                else
                                {
                                    walletAddress = await DataRepository
                                        .GetWalletAddressAsync(Currency, address)
                                        .ConfigureAwait(false);

                                    walletAddress.Balance = isConfirmed ? income + outcome : 0;
                                    walletAddress.UnconfirmedIncome = !isConfirmed ? income : 0;
                                    walletAddress.UnconfirmedOutcome = !isConfirmed ? outcome : 0;
                                    walletAddress.HasActivity = true;

                                    addressBalances.Add(address, walletAddress);
                                }

                                totalBalance += isConfirmed ? income + outcome : 0;
                                totalUnconfirmedIncome += !isConfirmed ? income : 0;
                                totalUnconfirmedOutcome += !isConfirmed ? outcome : 0;
                            }

                        }
                        catch (Exception e)
                        {
                            Log.Error(e, "Error in update balance");
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
            var erc20 = Erc20Config;

            var txs = (await DataRepository
                .GetTransactionsAsync(Currency, erc20.TransactionType)
                .ConfigureAwait(false))
                .Cast<EthereumTransaction>()
                .ToList();

            var walletAddress = await DataRepository
                .GetWalletAddressAsync(Currency, address)
                .ConfigureAwait(false);

            var balance = 0m;
            var unconfirmedIncome = 0m;
            var unconfirmedOutcome = 0m;

            foreach (var tx in txs)
            {
                var isIncome = address == tx.To;
                var isOutcome = address == tx.From;
                var isConfirmed = tx.IsConfirmed;
                var isFailed = tx.State == BlockchainTransactionState.Failed;

                var income = isIncome && !isFailed
                    ? erc20.TokenDigitsToTokens(tx.Amount)
                    : 0;

                var outcome = isOutcome && !isFailed
                    ? -erc20.TokenDigitsToTokens(tx.Amount)
                    : 0;

                balance += isConfirmed ? income + outcome : 0;
                unconfirmedIncome += !isConfirmed ? income : 0;
                unconfirmedOutcome += !isConfirmed ? outcome : 0;
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

            var selectedAddresses = (await SelectUnspentAddresses(
                    from: unspentAddresses,
                    amount: amount,
                    fee: fee,
                    feePrice: feePrice,
                    feeUsagePolicy: feeUsagePolicy,
                    addressUsagePolicy: addressUsagePolicy,
                    transactionType: transactionType)
                .ConfigureAwait(false))
                .ToList();

            if (toAddress != null && selectedAddresses.Any(a => a.WalletAddress.Address == toAddress))
            {
                selectedAddresses.RemoveAll(a => a.WalletAddress.Address != toAddress);
            }

            return ResolvePublicKeys(selectedAddresses
                .Select(w => w.WalletAddress)
                .ToList());
        }

        private async Task<IEnumerable<SelectedWalletAddress>> SelectUnspentAddresses(
            List<WalletAddress> from,
            decimal amount,
            decimal fee,
            decimal feePrice,
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType)
        {
            var erc20 = Erc20Config;
            var eth = EthConfig;

            if (addressUsagePolicy == AddressUsagePolicy.UseMinimalBalanceFirst)
            {
                from = from.ToList().SortList((a, b) => a.AvailableBalance().CompareTo(b.AvailableBalance()));
            }
            else if (addressUsagePolicy == AddressUsagePolicy.UseMaximumBalanceFirst)
            {
                from = from.ToList().SortList((a, b) => b.AvailableBalance().CompareTo(a.AvailableBalance()));
            }
            else if (addressUsagePolicy == AddressUsagePolicy.UseMaximumChainBalanceFirst)
            {
                var ethUnspentAddresses = (await DataRepository
                    .GetUnspentAddressesAsync(eth.Name)
                    .ConfigureAwait(false))
                    .ToList();

                if (!ethUnspentAddresses.Any())
                {
                    Log.Debug("Unsufficient ETH ammount for ERC20 token processing");
                    return Enumerable.Empty<SelectedWalletAddress>();
                }

                ethUnspentAddresses = ethUnspentAddresses.SortList((a, b) => b.AvailableBalance().CompareTo(a.AvailableBalance()));

                from = from.FindAll(
                    a => ethUnspentAddresses.Select(b => b.Address)
                        .ToList()
                        .Contains(a.Address));
            }

            else if (addressUsagePolicy == AddressUsagePolicy.UseOnlyOneAddress)
            {
                var result = new List<SelectedWalletAddress>();

                var feeInEth = feeUsagePolicy == FeeUsagePolicy.EstimatedFee
                    ? erc20.GetFeeAmount(
                        fee: GasLimitByType(transactionType, isFirstTx: true),
                        feePrice: await eth
                            .GetGasPriceAsync()
                            .ConfigureAwait(false))
                    : erc20.GetFeeAmount(fee, feePrice);

                //take erc20 non zero addresses first
                foreach (var address in from.TakeWhile(x => x.AvailableBalance() >= amount))
                {
                    var ethAddress = await DataRepository
                        .GetWalletAddressAsync(eth.Name, address.Address)
                        .ConfigureAwait(false);
                    
                    if (ethAddress == null || ethAddress.AvailableBalance() < feeInEth)
                    {
                        Log.Debug("Unsufficient ETH ammount for ERC20 token processing on address {@address} with available balance {@balance} and needed amount {@amount}",
                            ethAddress.Address,
                            ethAddress.AvailableBalance(),
                            feeInEth);
                        continue;
                    }

                    result.Add(new SelectedWalletAddress
                    {
                        WalletAddress = address,
                        UsedAmount = amount,
                        UsedFee = feeInEth
                    });
                }

                if (result.Any() || amount != 0m)
                    return result;
                
                //take non zero eth addresses
                var ethUnspentAddresses = (await DataRepository
                    .GetUnspentAddressesAsync(eth.Name)
                    .ConfigureAwait(false))
                    .ToList();

                if(!ethUnspentAddresses.Any())
                {
                    Log.Debug("Unsufficient ETH ammount for ERC20 token processing");
                    return Enumerable.Empty<SelectedWalletAddress>();
                }

                ethUnspentAddresses = ethUnspentAddresses.FindAll(a => a.AvailableBalance() > feeInEth);
                ethUnspentAddresses = ethUnspentAddresses.SortList((a, b) => a.AvailableBalance().CompareTo(b.AvailableBalance()));

                foreach (var address in ethUnspentAddresses)
                {
                    result.Add(new SelectedWalletAddress
                    {
                        WalletAddress = address,
                        UsedAmount = amount,
                        UsedFee = feeInEth
                    });
                }

                return result;
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

                    if (!(availableBalance > 0))
                        continue;

                    var ethAddress = await DataRepository
                        .GetWalletAddressAsync(eth.Name, address.Address)
                        .ConfigureAwait(false);

                    var ethAvailableBalance = ethAddress != null ? ethAddress.AvailableBalance() : 0;

                    var txFee = feeUsagePolicy == FeeUsagePolicy.EstimatedFee
                        ? eth.GetFeeAmount(
                            fee: GasLimitByType(transactionType, isFirstTx),
                            feePrice: await eth
                                .GetGasPriceAsync()
                                .ConfigureAwait(false))
                        : feeUsagePolicy == FeeUsagePolicy.FeeForAllTransactions
                            ? Math.Round(eth.GetFeeAmount(fee, feePrice) / txCount, eth.Digits)
                            : eth.GetFeeAmount(fee, feePrice);

                    if (ethAvailableBalance < txFee) // ignore address with balance less than fee
                    {
                        Log.Debug("Unsufficient ETH ammount for ERC20 token processing on address {@address} with available balance {@balance} and needed amount {@amount}",
                            ethAddress.Address,
                            ethAddress.AvailableBalance(),
                            txFee);
                        if (result.Count + from.Count - from.IndexOf(address) <= txCount)
                            break;
                        else
                            continue;
                    }

                    var amountToUse = Math.Min(availableBalance, requiredAmount);

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

        public override async Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return unspentAddresses.MaxBy(a => a.AvailableBalance());

            var unspentEthereumAddresses = await DataRepository
                .GetUnspentAddressesAsync("ETH")
                .ConfigureAwait(false);

            if (unspentEthereumAddresses.Any())
            {
                var ethereumAddress = unspentEthereumAddresses.MaxBy(a => a.AvailableBalance());

                return await DivideAddressAsync(
                    chain: ethereumAddress.KeyIndex.Chain,
                    index: ethereumAddress.KeyIndex.Index,
                    cancellationToken: cancellationToken);
            }

            var lastActiveAddress = await DataRepository
                .GetLastActiveWalletAddressAsync(
                    currency: "ETH",
                    chain: Bip44.External)
                .ConfigureAwait(false);

            return await DivideAddressAsync(
                    chain: Bip44.External,
                    index: lastActiveAddress?.KeyIndex.Index + 1 ?? 0,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return ResolvePublicKey(unspentAddresses.MaxBy(w => w.AvailableBalance()));

            var unspentEthereumAddresses = await DataRepository
                .GetUnspentAddressesAsync("ETH")
                .ConfigureAwait(false);

            if (unspentEthereumAddresses.Any())
            {
                var ethereumAddress = unspentEthereumAddresses.MaxBy(a => a.AvailableBalance());

                return await DivideAddressAsync(
                    chain: ethereumAddress.KeyIndex.Chain,
                    index: ethereumAddress.KeyIndex.Index,
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

            var redeemAddress = await GetFreeExternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            return ResolvePublicKey(redeemAddress);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentTokenAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        #endregion Addresses
    }
}