using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class Erc20Account : CurrencyAccount, IEstimatable
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
            string from,
            string to,
            decimal amount,
            decimal gasLimit = 0,
            decimal gasPrice = 0,
            bool useDefaultFee = false,
            CancellationToken cancellationToken = default)
        {
            //if (from == to)
            //    return new Error(
            //        code: Errors.SendingAndReceivingAddressesAreSame,
            //        description: "Sending and receiving addresses are the same.");

            var erc20Config = Erc20Config;

            if (useDefaultFee)
            {
                gasLimit = GasLimitByType(BlockchainTransactionType.Output);

                gasPrice = await erc20Config
                    .GetGasPriceAsync()
                    .ConfigureAwait(false);
            }

            var addressFeeUsage = await SelectUnspentAddressesAsync(
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

            if (gasLimit < erc20Config.TransferGasLimit)
                return new Error(
                    code: Errors.InsufficientGas,
                    description: "Insufficient gas");

            var feeAmount = erc20Config.GetFeeAmount(gasLimit, gasPrice);

            Log.Debug("Fee per transaction {@feePerTransaction}. Fee Amount {@feeAmount}",
                gasLimit,
                feeAmount);

            Log.Debug("Send {@amount} of {@currency} from address {@address} with available balance {@balance}",
                addressFeeUsage.UsedAmount,
                erc20Config.Name,
                addressFeeUsage.WalletAddress.Address,
                addressFeeUsage.WalletAddress.AvailableBalance());

            using var addressLock = await EthereumAccount.AddressLocker
                .GetLockAsync(addressFeeUsage.WalletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            var nonceResult = await EthereumNonceManager.Instance
                .GetNonceAsync(EthConfig, addressFeeUsage.WalletAddress.Address)
                .ConfigureAwait(false);

            if (nonceResult.HasError)
                return nonceResult.Error;

            TransactionInput txInput;

            var message = new ERC20TransferFunctionMessage
            {
                To          = to.ToLowerInvariant(),
                Value       = erc20Config.TokensToTokenDigits(addressFeeUsage.UsedAmount),
                FromAddress = addressFeeUsage.WalletAddress.Address,
                Gas         = new BigInteger(gasLimit),
                GasPrice    = new BigInteger(EthereumConfig.GweiToWei(gasPrice)),
                Nonce       = nonceResult.Value
            };

            txInput = message.CreateTransactionInput(erc20Config.ERC20ContractAddress);

            var tx = new EthereumTransaction(erc20Config.Name, txInput)
            {
                Type = BlockchainTransactionType.Output
            };

            var signResult = await Wallet
                .SignAsync(tx, addressFeeUsage.WalletAddress, erc20Config, cancellationToken)
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

            tx.Amount = erc20Config.TokensToTokenDigits(addressFeeUsage.UsedAmount);
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

            _ = UpdateBalanceAsync(cancellationToken);

            return null;
        }

        public async Task<decimal> EstimateFeeAsync(
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default)
        {
            var gasPrice = await EthConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            return EthConfig.GetFeeAmount(GasLimitByType(type), gasPrice);
        }

        public async Task<decimal?> EstimateFeeAsync(
            IFromSource from,
            string to,
            decimal amount,
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default)
        {
            return await EstimateFeeAsync(
                    type: type,
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
                    Error = new Error(Errors.FromAddressIsNullOrEmpty, "\"From\" address is null or empty")
                };

            //if (from == to)
            //    return new MaxAmountEstimation {
            //        Error = new Error(Errors.SendingAndReceivingAddressesAreSame, "Sending and receiving addresses are same")
            //    };

            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return new MaxAmountEstimation {
                    Error = new Error(Errors.AddressNotFound, "Address not found")
                };

            var eth = EthConfig;

            var estimatedGasPrice = await eth
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var reserveFeeInEth = ReserveFee(estimatedGasPrice);

            var feeInEth = eth.GetFeeAmount(
                gasLimit == null
                    ? GasLimitByType(type)
                    : gasLimit.Value,
                gasPrice == null
                    ? estimatedGasPrice
                    : gasPrice.Value);

            var requiredFeeInEth = feeInEth + (reserve ? reserveFeeInEth : 0);

            var ethAddress = await DataRepository
                .GetWalletAddressAsync(eth.Name, fromAddress.Address)
                .ConfigureAwait(false);

            if (ethAddress == null)
                return new MaxAmountEstimation
                {
                    Fee      = requiredFeeInEth,
                    Reserved = reserveFeeInEth,
                    Error    = new Error(
                        Errors.InsufficientChainFunds,
                        string.Format(CultureInfo.CurrentCulture,
                        "Insufficient {0} to cover token transfer fee",
                        Erc20Config.FeeCurrencyName))
                };

            var restBalanceInEth = ethAddress.AvailableBalance() - requiredFeeInEth;

            if (restBalanceInEth < 0)
                return new MaxAmountEstimation {
                    Fee      = requiredFeeInEth,
                    Reserved = reserveFeeInEth,
                    Error    = new Error(
                        Errors.InsufficientChainFunds,
                        string.Format(CultureInfo.CurrentCulture,
                        "Insufficient {0} to cover token transfer fee",
                        Erc20Config.FeeCurrencyName))
                };

            if (fromAddress.AvailableBalance() <= 0)
                return new MaxAmountEstimation {
                    Fee      = requiredFeeInEth,
                    Reserved = reserveFeeInEth,
                    Error    = new Error(Errors.InsufficientFunds, "Insufficient funds")
                };

            return new MaxAmountEstimation
            {
                Amount   = fromAddress.AvailableBalance(),
                Fee      = feeInEth,
                Reserved = reserveFeeInEth
            };
        }

        public Task<MaxAmountEstimation> EstimateMaxAmountToSendAsync(
            IFromSource from,
            string to,
            BlockchainTransactionType type,
            decimal? fee,
            decimal? feePrice,
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            var fromAddress = (from as FromAddress)?.Address;

            return EstimateMaxAmountToSendAsync(
                from: fromAddress,
                type: type,
                gasLimit: fee,
                gasPrice: feePrice,
                reserve: reserve,
                cancellationToken: cancellationToken);
        }

        private decimal GasLimitByType(BlockchainTransactionType type)
        {
            var erc20 = Erc20Config;

            if (type.HasFlag(BlockchainTransactionType.TokenApprove))
                return erc20.ApproveGasLimit;

            if (type.HasFlag(BlockchainTransactionType.SwapPayment)) // todo: recheck
                return erc20.ApproveGasLimit * 2 + erc20.InitiateWithRewardGasLimit;

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

            if (tx is not EthereumTransaction ethTx)
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
                    var totalUnconfirmedIncome = 0m;
                    var totalUnconfirmedOutcome = 0m;

                    var addresses = new Dictionary<string, WalletAddress>();

                    foreach (var tx in txs)
                    {
                        try
                        {
                            var selfAddresses = new HashSet<string>();

                            if (tx.Type.HasFlag(BlockchainTransactionType.Input))
                                selfAddresses.Add(tx.To);

                            if (tx.Type.HasFlag(BlockchainTransactionType.Output))
                                selfAddresses.Add(tx.From);

                            foreach (var address in selfAddresses)
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

                                if (addresses.TryGetValue(address, out var walletAddress))
                                {
                                    //walletAddress.Balance += isConfirmed ? income + outcome : 0;
                                    walletAddress.UnconfirmedIncome += !isConfirmed ? income : 0;
                                    walletAddress.UnconfirmedOutcome += !isConfirmed ? outcome : 0;
                                }
                                else
                                {
                                    walletAddress = await DataRepository
                                        .GetWalletAddressAsync(Currency, address)
                                        .ConfigureAwait(false);

                                    //walletAddress.Balance = isConfirmed ? income + outcome : 0;
                                    walletAddress.UnconfirmedIncome = !isConfirmed ? income : 0;
                                    walletAddress.UnconfirmedOutcome = !isConfirmed ? outcome : 0;
                                    walletAddress.HasActivity = true;

                                    addresses.Add(address, walletAddress);
                                }

                                //totalBalance += isConfirmed ? income + outcome : 0;
                                totalUnconfirmedIncome += !isConfirmed ? income : 0;
                                totalUnconfirmedOutcome += !isConfirmed ? outcome : 0;
                            }

                        }
                        catch (Exception e)
                        {
                            Log.Error(e, "Error in update balance");
                        }
                    }

                    var totalBalance = 0m;

                    var api = erc20.BlockchainApi as IEthereumBlockchainApi;

                    foreach (var wa in addresses.Values)
                    {
                        var balanceResult = await api
                            .TryGetErc20BalanceAsync(
                                address: wa.Address,
                                contractAddress: erc20.ERC20ContractAddress,
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

                        wa.Balance = erc20.TokenDigitsToTokens(balanceResult.Value);

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
                    Log.Error(e, $"{Currency} UpdateBalanceAsync error.");
                }

            }, cancellationToken);
        }

        public override Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async() =>
            {
                try
                {
                    var erc20 = Erc20Config;

                    var walletAddress = await DataRepository
                        .GetWalletAddressAsync(Currency, address)
                        .ConfigureAwait(false);

                    if (walletAddress == null)
                        return;

                    var api = erc20.BlockchainApi as IEthereumBlockchainApi;

                    var balanceResult = await api
                        .TryGetErc20BalanceAsync(
                            address: address,
                            contractAddress: erc20.ERC20ContractAddress,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (balanceResult.HasError)
                    {
                        Log.Error("Error while balance update for {@address} with code {@code} and description {@description}",
                            address,
                            balanceResult.Error.Code,
                            balanceResult.Error.Description);

                        return;
                    }

                    var balance = erc20.TokenDigitsToTokens(balanceResult.Value);

                    var unconfirmedIncome = 0m;
                    var unconfirmedOutcome = 0m;

                    // calculate unconfirmed balances
                    var unconfirmedTxs = (await DataRepository
                        .GetUnconfirmedTransactionsAsync(Currency, erc20.TransactionType)
                        .ConfigureAwait(false))
                        .Cast<EthereumTransaction>()
                        .ToList();

                    foreach (var utx in unconfirmedTxs)
                    {
                        var isIncome = address == utx.To;
                        var isOutcome = address == utx.From;
                        var isFailed = utx.State == BlockchainTransactionState.Failed;

                        unconfirmedIncome += isIncome && !isFailed
                            ? erc20.TokenDigitsToTokens(utx.Amount)
                            : 0;
                        unconfirmedOutcome += isOutcome && !isFailed
                            ? -erc20.TokenDigitsToTokens(utx.Amount)
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

                        LoadBalances();

                        RaiseBalanceUpdated(new CurrencyEventArgs(Currency));
                    }
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

        #endregion Balances

        #region Addresses

        private async Task<SelectedWalletAddress> SelectUnspentAddressesAsync(
            string from,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default)
        {
            var erc20 = Erc20Config;
            var eth = EthConfig;

            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return null; // invalid address

            var feeInEth = erc20.GetFeeAmount(fee, feePrice);

            var ethAddress = await DataRepository
                .GetWalletAddressAsync(eth.Name, fromAddress.Address)
                .ConfigureAwait(false);

            var availableBalanceInEth = ethAddress?.AvailableBalance() ?? 0m;

            if (availableBalanceInEth < feeInEth)
            {
                Log.Debug("Unsufficient ETH ammount for ERC20 token processing on address {@address} with available balance {@balance} and needed amount {@amount}",
                    ethAddress.Address,
                    availableBalanceInEth,
                    feeInEth);

                return null; // insufficient funds
            }

            var restBalanceInTokens = fromAddress.AvailableBalance() - amount;

            if (restBalanceInTokens < 0) // todo: log?
                return null;

            return new SelectedWalletAddress
            {
                WalletAddress = fromAddress,
                UsedAmount    = amount,
                UsedFee       = feeInEth
            };
        }

        public override async Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            // addresses with tokens
            var unspentAddresses = await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return unspentAddresses.MaxBy(a => a.AvailableBalance());

            // addresses with eth
            var unspentEthereumAddresses = await DataRepository
                .GetUnspentAddressesAsync("ETH")
                .ConfigureAwait(false);

            if (unspentEthereumAddresses.Any())
            {
                var ethereumAddress = unspentEthereumAddresses.MaxBy(a => a.AvailableBalance());

                return await DivideAddressAsync(
                    keyIndex: ethereumAddress.KeyIndex,
                    keyType: ethereumAddress.KeyType);
            }

            // last active address
            var lastActiveAddress = await DataRepository
                .GetLastActiveWalletAddressAsync(
                    currency: "ETH",
                    chain: Bip44.External,
                    keyType: CurrencyConfig.StandardKey)
                .ConfigureAwait(false);

            return await DivideAddressAsync(
                    account: Bip44.DefaultAccount,
                    chain: Bip44.External,
                    index: lastActiveAddress?.KeyIndex.Index + 1 ?? 0,
                    keyType: CurrencyConfig.StandardKey)
                .ConfigureAwait(false);
        }

        public async Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default)
        {
            // addresses with tokens
            var unspentAddresses = await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return ResolvePublicKey(unspentAddresses.MaxBy(w => w.AvailableBalance()));

            // addresses with eth
            var unspentEthereumAddresses = await DataRepository
                .GetUnspentAddressesAsync("ETH")
                .ConfigureAwait(false);

            if (unspentEthereumAddresses.Any())
            {
                var ethereumAddress = unspentEthereumAddresses.MaxBy(a => a.AvailableBalance());

                return await DivideAddressAsync(
                    keyIndex: ethereumAddress.KeyIndex,
                    keyType: ethereumAddress.KeyType);
            }

            foreach (var chain in new[] { Bip44.Internal, Bip44.External })
            {
                var lastActiveAddress = await DataRepository
                    .GetLastActiveWalletAddressAsync(
                        currency: Currency,
                        chain: chain,
                        keyType: CurrencyConfig.StandardKey)
                    .ConfigureAwait(false);

                if (lastActiveAddress != null)
                    return ResolvePublicKey(lastActiveAddress);
            }

            var redeemAddress = await GetFreeExternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            return ResolvePublicKey(redeemAddress);
        }

        #endregion Addresses
    }
}