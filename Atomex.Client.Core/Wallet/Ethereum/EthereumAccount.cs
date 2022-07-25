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
using Atomex.EthereumTokens;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;

namespace Atomex.Wallet.Ethereum
{
    public class EthereumAccount : CurrencyAccount, IEstimatable, IHasTokens
    {
        private static ResourceLocker<string> _addressLocker;
        public static ResourceLocker<string> AddressLocker
        {
            get {
                var instance = _addressLocker;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _addressLocker, new ResourceLocker<string>(), null);
                    instance = _addressLocker;
                }

                return instance;
            }
        }

        public EthereumAccount(
            string currency,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository)
                : base(currency, currencies, wallet, dataRepository)
        {
        }

        #region Common

        private EthereumConfig EthConfig => Currencies.Get<EthereumConfig>(Currency);
        private Erc20Config Erc20Config => Currencies.Get<Erc20Config>("USDT");

        public async Task<Error> SendAsync(
            string from,
            string to,
            decimal amount,
            decimal gasLimit,
            decimal gasPrice,
            bool useDefaultFee = false,
            CancellationToken cancellationToken = default)
        {
            //if (from == to)
            //    return new Error(
            //        code: Errors.SendingAndReceivingAddressesAreSame,
            //        description: "Sending and receiving addresses are the same.");

            var ethConfig = EthConfig;

            if (useDefaultFee)
            {
                gasLimit = GasLimitByType(BlockchainTransactionType.Output);

                gasPrice = Math.Floor(await ethConfig
                    .GetGasPriceAsync(cancellationToken)
                    .ConfigureAwait(false));
            }

            var addressFeeUsage = await CalculateFundsUsageAsync(
                    from: from,
                    amount: amount,
                    fee: gasLimit,
                    feePrice: gasPrice,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (addressFeeUsage == null)
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: "Insufficient funds");

            if (gasLimit < ethConfig.GasLimit)
                return new Error(
                    code: Errors.InsufficientGas,
                    description: "Insufficient gas");

            Log.Debug("Try to send {@amount} ETH with fee {@fee} from address {@address} with available balance {@balance}",
                addressFeeUsage.UsedAmount,
                addressFeeUsage.UsedFee,
                addressFeeUsage.WalletAddress.Address,
                addressFeeUsage.WalletAddress.AvailableBalance());

            // lock address to prevent nonce races
            using var addressLock = await AddressLocker
                .GetLockAsync(addressFeeUsage.WalletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            var nonceAsyncResult = await EthereumNonceManager.Instance
                .GetNonceAsync(ethConfig, addressFeeUsage.WalletAddress.Address, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (nonceAsyncResult.HasError)
                return nonceAsyncResult.Error;

            var tx = new EthereumTransaction
            {
                Currency     = ethConfig.Name,
                Type         = BlockchainTransactionType.Output,
                CreationTime = DateTime.UtcNow,
                From         = addressFeeUsage.WalletAddress.Address,
                To           = to.ToLowerInvariant(),
                Amount       = EthereumConfig.EthToWei(addressFeeUsage.UsedAmount),
                Nonce        = nonceAsyncResult.Value,
                GasPrice     = new BigInteger(EthereumConfig.GweiToWei(gasPrice)),
                GasLimit     = new BigInteger(gasLimit),
            };

            var signResult = await Wallet
                .SignAsync(tx, addressFeeUsage.WalletAddress, ethConfig, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
                return new Error(
                    code: Errors.TransactionSigningError,
                    description: "Transaction signing error");

            if (!tx.Verify())
                return new Error(
                    code: Errors.TransactionVerificationError,
                    description: "Transaction verification error");

            var broadcastResult = await ethConfig.BlockchainApi
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

            _ = UpdateBalanceAsync(cancellationToken);

            return null;
        }

        public async Task<decimal> EstimateFeeAsync(
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default)
        {
            var gasPrice = Math.Floor(await EthConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false));

            return EthConfig.GetFeeAmount(GasLimitByType(type), gasPrice);
        }

        public async Task<decimal?> EstimateSwapPaymentFeeAsync(
            IFromSource from,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            return await EstimateFeeAsync(
                    type: BlockchainTransactionType.SwapPayment,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<MaxAmountEstimation> EstimateMaxAmountToSendAsync(
            string from,
            BlockchainTransactionType type,
            decimal? gasLimit,
            decimal? gasPrice,
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

            var eth = EthConfig;

            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return new MaxAmountEstimation {
                    Error = new Error(Errors.AddressNotFound, Resources.AddressNotFoundInLocalDb)
                };

            var estimatedGasPrice = Math.Floor(await eth
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false));

            var feeInEth = eth.GetFeeAmount(
                gasLimit == null
                    ? GasLimitByType(type)
                    : gasLimit.Value,
                gasPrice == null
                    ? estimatedGasPrice
                    : gasPrice.Value);

            if (feeInEth == 0)
                return new MaxAmountEstimation {
                    Error = new Error(Errors.InsufficientFee, Resources.TooLowFees)
                };

            var reserveFeeInEth = ReserveFee(estimatedGasPrice);

            var requiredFeeInEth = feeInEth + (reserve ? reserveFeeInEth : 0);

            var restAmountInEth = fromAddress.AvailableBalance() - requiredFeeInEth;

            if (restAmountInEth < 0)
                return new MaxAmountEstimation {
                    Amount   = restAmountInEth,
                    Fee      = requiredFeeInEth,
                    Reserved = reserveFeeInEth,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        description: Resources.InsufficientFundsToCoverFees,
                        details: string.Format(
                            Resources.InsufficientFundsToCoverFeesDetails,
                            requiredFeeInEth,
                            Currency,
                            fromAddress.AvailableBalance()))
                };

            return new MaxAmountEstimation
            {
                Amount   = restAmountInEth,
                Fee      = feeInEth,
                Reserved = reserveFeeInEth
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
                type: BlockchainTransactionType.SwapPayment,
                gasLimit: null,
                gasPrice: null,
                reserve: reserve,
                cancellationToken: cancellationToken);
        }

        private decimal GasLimitByType(BlockchainTransactionType type)
        {
            var eth = EthConfig;

            if (type.HasFlag(BlockchainTransactionType.SwapPayment))
                return eth.InitiateWithRewardGasLimit;

            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return eth.RefundGasLimit;

            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return eth.RedeemGasLimit;

            return eth.GasLimit;
        }

        private decimal ReserveFee(decimal gasPrice)
        {
            var ethConfig = EthConfig;
            var erc20Config = Erc20Config;

            return Math.Max(
                ethConfig.GetFeeAmount(Math.Max(erc20Config.RefundGasLimit, erc20Config.RedeemGasLimit), gasPrice),
                ethConfig.GetFeeAmount(Math.Max(ethConfig.RefundGasLimit, ethConfig.RedeemGasLimit), gasPrice));
        }

        protected override async Task<bool> ResolveTransactionTypeAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var ethConfig = EthConfig;

            if (tx is not EthereumTransaction ethTx)
                throw new ArgumentException("Invalid tx type", nameof(tx));

            var oldTx = !ethTx.IsInternal
                ? await DataRepository
                    .GetTransactionByIdAsync<EthereumTransaction>(Currency, tx.Id)
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

                var isToSwapContract = ethTx.To == ethConfig.SwapContractAddress;

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
                    var eth = EthConfig;

                    var txs = (await DataRepository
                        .GetTransactionsAsync<EthereumTransaction>(Currency)
                        .ConfigureAwait(false))
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
                                ? EthereumConfig.WeiToEth(tx.Amount)
                                : 0;

                            var outcome = isOutcome
                                ? (!isFailed
                                    ? -EthereumConfig.WeiToEth(tx.Amount + tx.GasPrice * (tx.GasUsed != 0 ? tx.GasUsed : tx.GasLimit))
                                    : -EthereumConfig.WeiToEth(tx.GasPrice * tx.GasUsed))
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

        public override Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var eth = EthConfig;

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
                        .GetUnconfirmedTransactionsAsync<EthereumTransaction>(Currency)
                        .ConfigureAwait(false))
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
                            ? EthereumConfig.WeiToEth(utx.Amount)
                            : 0;
                        unconfirmedOutcome += address == utx.From && !isFailed
                            ? -EthereumConfig.WeiToEth(utx.Amount + utx.GasPrice * (utx.GasUsed != 0 ? utx.GasUsed : utx.GasLimit))
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
                    Log.Debug("UpdateBalanceAsync canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, "UpdateBalanceAsync error.");
                }

            }, cancellationToken);
        }

        #endregion Balances

        #region Addresses

        public async Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return unspentAddresses.MaxBy(w => w.AvailableBalance());

            foreach (var chain in new[] { Bip44.Internal, Bip44.External })
            {
                var lastActiveAddress = await DataRepository
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
            var result = new List<WalletAddress>();

            foreach (var token in Atomex.Currencies.EthTokens)
            {
                var addresses = await DataRepository
                    .GetUnspentAddressesAsync(token)
                    .ConfigureAwait(false);

                result.AddRange(addresses);
            }

            return result;
        }

        private async Task<SelectedWalletAddress> CalculateFundsUsageAsync(
            string from,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default)
        {
            var eth = EthConfig;

            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return null; // invalid address

            var feeInEth = eth.GetFeeAmount(fee, feePrice);

            var restBalanceInEth = fromAddress.AvailableBalance() -
               amount -
               feeInEth;

            if (restBalanceInEth < 0)
                return null; // insufficient funds

            return new SelectedWalletAddress
            {
                WalletAddress = fromAddress,
                UsedAmount    = amount,
                UsedFee       = feeInEth
            };
        }

        #endregion Addresses

        #region Transactions

        public override async Task<IEnumerable<IBlockchainTransaction>> GetUnconfirmedTransactionsAsync(
            CancellationToken cancellationToken = default)
        {
            return await DataRepository
                .GetUnconfirmedTransactionsAsync<EthereumTransaction>(Currency)
                .ConfigureAwait(false);
        }

        #endregion Transactions
    }
}