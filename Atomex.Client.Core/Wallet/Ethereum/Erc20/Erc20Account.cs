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
using Atomex.Blockchain.Ethereum.Erc20;
using Atomex.Blockchain.Ethereum.Erc20.Messages;
using Atomex.Common;
using Atomex.Core;
using Atomex.EthereumTokens;
using Atomex.Wallet.Abstract;
using Atomex.Wallets.Bips;
using Error = Atomex.Common.Error;

namespace Atomex.Wallet.Ethereum
{
    public class Erc20Account : CurrencyAccount, IEstimatable
    {
        protected readonly EthereumAccount _ethereumAccount;

        public Erc20Account(
            string currency,
            ICurrencies currencies,
            IHdWallet wallet,
            ILocalStorage localStorage,
            EthereumAccount ethereumAccount)
                : base(currency, currencies, wallet, localStorage)
        {
            _ethereumAccount = ethereumAccount ?? throw new ArgumentNullException(nameof(ethereumAccount));
        }

        #region Common

        private Erc20Config Erc20Config => Currencies.Get<Erc20Config>(Currency);
        private EthereumConfig EthConfig => Currencies.Get<EthereumConfig>("ETH");

        public async Task<Result<string>> SendAsync(
            string from,
            string to,
            BigInteger amount,
            long gasLimit = 0,
            decimal maxFeePerGas = 0,
            decimal maxPriorityFeePerGas = 0,
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
                gasLimit = GasLimitByType(TransactionType.Output);

                var (gasPrice, gasPriceError) = await erc20Config
                    .GetGasPriceAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (gasPriceError != null)
                    return gasPriceError;

                maxFeePerGas = gasPrice.MaxFeePerGas;
                maxPriorityFeePerGas = gasPrice.MaxPriorityFeePerGas;
            }

            var addressFeeUsage = await CalculateFundsUsageAsync(
                    from: from,
                    amount: amount,
                    gasLimit: gasLimit,
                    gasPrice: maxFeePerGas,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (addressFeeUsage == null)
                return new Error(
                    code: Errors.InsufficientFunds,
                    message: "Insufficient funds");

            if (gasLimit < erc20Config.TransferGasLimit)
                return new Error(
                    code: Errors.InsufficientGas,
                    message: "Insufficient gas");

            var feeAmountInWei = gasLimit * EthereumHelper.GweiToWei(maxFeePerGas);

            Log.Debug("Fee per transaction {@feePerTransaction}. Fee Amount {@feeAmount}",
                gasLimit,
                feeAmountInWei);

            Log.Debug("Send {@amount} of {@currency} from address {@address} with available balance {@balance}",
                addressFeeUsage.UsedAmount,
                erc20Config.Name,
                addressFeeUsage.WalletAddress.Address,
                addressFeeUsage.WalletAddress.AvailableBalance());

            using var addressLock = await EthereumAccount.AddressLocker
                .GetLockAsync(addressFeeUsage.WalletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            var api = EthConfig.GetEtherScanApi();

            var (nonce, nonceError) = await EthereumNonceManager.Instance
                .GetNonceAsync(api, addressFeeUsage.WalletAddress.Address, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (nonceError != null)
                return nonceError;

            TransactionInput txInput;

            var message = new Erc20TransferMessage
            {
                To                   = to.ToLowerInvariant(),
                Value                = addressFeeUsage.UsedAmount,
                FromAddress          = addressFeeUsage.WalletAddress.Address,
                Gas                  = gasLimit,
                MaxFeePerGas         = EthereumHelper.GweiToWei(maxFeePerGas),
                MaxPriorityFeePerGas = EthereumHelper.GweiToWei(maxPriorityFeePerGas),
                Nonce                = nonce,
                TransactionType      = EthereumHelper.Eip1559TransactionType
            };

            txInput = message.CreateTransactionInput(erc20Config.ERC20ContractAddress);

            var txRequest = new EthereumTransactionRequest(txInput, EthConfig.ChainId);

            var signResult = await _ethereumAccount
                .SignAsync(txRequest, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
                return new Error(
                    code: Errors.TransactionSigningError,
                    message: "Transaction signing error");

            if (!txRequest.Verify())
                return new Error(
                    code: Errors.TransactionVerificationError,
                    message: "Transaction verification error");

            var (txId, broadcastError) = await api
                .BroadcastAsync(txRequest, cancellationToken)
                .ConfigureAwait(false);

            if (broadcastError != null)
                return broadcastError;

            if (txId == null)
                return new Error(
                    code: Errors.TransactionBroadcastError,
                    message: "Transaction Id is null");

            Log.Debug("Transaction successfully sent with txId: {@id}", txId);

            var tx = new EthereumTransaction(txRequest);

            await LocalStorage
                .UpsertTransactionAsync(
                    tx: tx,
                    notifyIfNewOrChanged: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return txId;
        }

        public async Task<Result<decimal>> EstimateFeeAsync(
            TransactionType type,
            CancellationToken cancellationToken = default)
        {
            var (gasPrice, error) = await EthConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            return EthConfig.GetFeeInEth(GasLimitByType(type), gasPrice.MaxFeePerGas);
        }

        public async Task<Result<decimal>> EstimateSwapPaymentFeeAsync(
            IFromSource from,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            return await EstimateFeeAsync(
                    type: TransactionType.SwapPayment,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<MaxAmountEstimation> EstimateMaxAmountToSendAsync(
            string from,
            TransactionType type,
            long? gasLimit,
            decimal? maxFeePerGas,
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

            var tokenAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (tokenAddress == null)
                return new MaxAmountEstimation {
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFunds),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsDetails,
                        0,                // available tokens
                        Erc20Config.Name) // currency code
                };

            var eth = EthConfig;

            var (estimatedGasPrice, estimateError) = await eth
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (estimateError != null)
                return new MaxAmountEstimation
                {
                    Error = estimateError
                };

            var reserveFeeInWei = ReserveFeeInWei(estimatedGasPrice.MaxFeePerGas);

            var feeInWei = (gasLimit == null ? GasLimitByType(type) : gasLimit.Value) *
                EthereumHelper.GweiToWei(maxFeePerGas == null ? estimatedGasPrice.MaxFeePerGas : maxFeePerGas.Value);

            if (feeInWei == 0)
                return new MaxAmountEstimation {
                    Error = new Error(Errors.InsufficientFee, Resources.TooLowFees)
                };

            var requiredFeeInWei = feeInWei + (reserve ? reserveFeeInWei : 0);

            var ethAddress = await LocalStorage
                .GetWalletAddressAsync(eth.Name, tokenAddress.Address)
                .ConfigureAwait(false);

            if (ethAddress == null)
                return new MaxAmountEstimation
                {
                    Fee = requiredFeeInWei,
                    Reserved = reserveFeeInWei,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFundsToCoverFees),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsToCoverFeesDetails,
                        requiredFeeInWei,            // required fee
                        Erc20Config.FeeCurrencyName, // currency code
                        0m)                          // available
                };

            var restBalanceInWei = ethAddress.AvailableBalance() - requiredFeeInWei;

            if (restBalanceInWei < 0)
                return new MaxAmountEstimation {
                    Fee = requiredFeeInWei,
                    Reserved = reserveFeeInWei,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFundsToCoverFees),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsToCoverFeesDetails,
                        requiredFeeInWei,              // required fee
                        Erc20Config.FeeCurrencyName,   // currency code
                        ethAddress.AvailableBalance()) // available
                };

            if (tokenAddress.AvailableBalance() <= 0)
                return new MaxAmountEstimation {
                    Fee = requiredFeeInWei,
                    Reserved = reserveFeeInWei,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFunds),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsDetails,
                        tokenAddress.AvailableBalance(), // available tokens
                        Erc20Config.Name)                // currency code
                };

            return new MaxAmountEstimation
            {
                Amount = tokenAddress.AvailableBalance(),
                Fee = requiredFeeInWei,
                Reserved = reserveFeeInWei
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
                type: TransactionType.SwapPayment,
                gasLimit: null,
                maxFeePerGas: null,
                reserve: reserve,
                cancellationToken: cancellationToken);
        }

        private long GasLimitByType(TransactionType type)
        {
            var erc20 = Erc20Config;

            if (type.HasFlag(TransactionType.TokenApprove))
                return erc20.ApproveGasLimit;

            if (type.HasFlag(TransactionType.SwapPayment)) // todo: recheck
                return erc20.ApproveGasLimit * 2 + erc20.InitiateWithRewardGasLimit;

            if (type.HasFlag(TransactionType.SwapRefund))
                return erc20.RefundGasLimit;

            if (type.HasFlag(TransactionType.SwapRedeem))
                return erc20.RedeemGasLimit;

            return erc20.TransferGasLimit;
        }

        private BigInteger ReserveFeeInWei(decimal gasPrice)
        {
            var eth = EthConfig;
            var erc20 = Erc20Config;

            var maxGasLimit = Math.Max(Math.Max(erc20.RefundGasLimit, erc20.RedeemGasLimit), Math.Max(eth.RefundGasLimit, eth.RedeemGasLimit));

            return maxGasLimit * EthereumHelper.GweiToWei(gasPrice);
        }

        #endregion Common

        #region Balances

        public override async Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            var scanner = new Erc20WalletScanner(this, _ethereumAccount);

            await scanner
                .UpdateBalanceAsync(skipUsed: false, cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var scanner = new Erc20WalletScanner(this, _ethereumAccount);

            await scanner
                .UpdateBalanceAsync(address, cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion Balances

        #region Addresses

        private async Task<SelectedWalletAddress> CalculateFundsUsageAsync(
            string from,
            BigInteger amount,
            long gasLimit,
            decimal gasPrice,
            CancellationToken cancellationToken = default)
        {
            var eth = EthConfig;

            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return null; // invalid address

            var feeInWei = gasLimit * EthereumHelper.GweiToWei(gasPrice);

            var ethAddress = await LocalStorage
                .GetWalletAddressAsync(eth.Name, fromAddress.Address)
                .ConfigureAwait(false);

            var availableBalanceInWei = ethAddress?.AvailableBalance() ?? 0;

            if (availableBalanceInWei < feeInWei)
            {
                Log.Debug("Unsufficient ETH ammount for ERC20 token processing on address {@address} with available balance {@balance} and needed amount {@amount}",
                    ethAddress.Address,
                    availableBalanceInWei,
                    feeInWei);

                return null; // insufficient funds
            }

            var restBalanceInTokenDigits = fromAddress.AvailableBalance() - amount;

            if (restBalanceInTokenDigits < 0) // todo: log?
                return null;

            return new SelectedWalletAddress
            {
                WalletAddress = fromAddress,
                UsedAmount = amount,
                UsedFee = feeInWei
            };
        }

        public override async Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            // addresses with tokens
            var unspentAddresses = await LocalStorage
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return unspentAddresses.MaxBy(a => a.AvailableBalance());

            // addresses with eth
            var unspentEthereumAddresses = await LocalStorage
                .GetUnspentAddressesAsync("ETH")
                .ConfigureAwait(false);

            if (unspentEthereumAddresses.Any())
            {
                var ethereumAddress = unspentEthereumAddresses.MaxBy(a => a.AvailableBalance());

                return await DivideAddressAsync(
                    keyPath: ethereumAddress.KeyPath,
                    keyType: ethereumAddress.KeyType);
            }

            var keyType = CurrencyConfig.StandardKey;

            // last active ETH address
            var keyPathPattern = EthConfig
                .GetKeyPathPattern(keyType)
                .Replace(KeyPathExtensions.ChainPattern, Bip44.External.ToString());

            var lastActiveAddress = await LocalStorage
                .GetLastActiveWalletAddressAsync(
                    currency: EthConfig.Name,
                    keyPathPattern: keyPathPattern,
                    keyType: keyType)
                .ConfigureAwait(false);

            var keyPath = lastActiveAddress != null
                ? lastActiveAddress.KeyPath.SetIndex(
                    keyPathPattern: keyPathPattern,
                    indexPattern: KeyPathExtensions.IndexPattern,
                    indexValue: $"{lastActiveAddress.KeyIndex + 1}")
                : keyPathPattern
                    .Replace(KeyPathExtensions.AccountPattern, KeyPathExtensions.DefaultAccount)
                    .Replace(KeyPathExtensions.IndexPattern, KeyPathExtensions.DefaultIndex);

            return await DivideAddressAsync(
                    keyPath: keyPath,
                    keyType: keyType)
                .ConfigureAwait(false);
        }

        public async Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default)
        {
            // addresses with tokens
            var unspentAddresses = await LocalStorage
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return unspentAddresses.MaxBy(w => w.AvailableBalance());

            // addresses with eth
            var unspentEthereumAddresses = await LocalStorage
                .GetUnspentAddressesAsync("ETH")
                .ConfigureAwait(false);

            if (unspentEthereumAddresses.Any())
            {
                var ethereumAddress = unspentEthereumAddresses.MaxBy(a => a.AvailableBalance());

                return await DivideAddressAsync(
                    keyPath: ethereumAddress.KeyPath,
                    keyType: ethereumAddress.KeyType);
            }
            
            var keyType = CurrencyConfig.StandardKey;

            foreach (var chain in new[] { Bip44.Internal, Bip44.External })
            {
                var keyPathPattern = Erc20Config
                    .GetKeyPathPattern(keyType)
                    .Replace(KeyPathExtensions.ChainPattern, chain.ToString());

                var lastActiveAddress = await LocalStorage
                    .GetLastActiveWalletAddressAsync(
                        currency: Currency,
                        keyPathPattern: keyPathPattern,
                        keyType: keyType)
                    .ConfigureAwait(false);

                if (lastActiveAddress != null)
                    return lastActiveAddress;
            }

            return await GetFreeExternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion Addresses

        #region Transactions

        public override async Task<IEnumerable<ITransaction>> GetUnconfirmedTransactionsAsync(
            CancellationToken cancellationToken = default)
        {
            return await LocalStorage
                .GetUnconfirmedTransactionsAsync<EthereumTransaction>(Currency)
                .ConfigureAwait(false);
        }

        public override async Task ResolveTransactionsMetadataAsync(
            IEnumerable<ITransaction> txs,
            CancellationToken cancellationToken = default)
        {
            var resolvedMetadata = new List<ITransactionMetadata>();

            foreach (var tx in txs.Cast<Erc20Transaction>())
            {
                var metadata = await ResolveTransactionMetadataAsync(tx, cancellationToken)
                    .ConfigureAwait(false);

                resolvedMetadata.Add(metadata);
            }

            await LocalStorage
                .UpsertTransactionsMetadataAsync(
                    resolvedMetadata,
                    notifyIfNewOrChanged: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Erc20TransactionMetadata> ResolveTransactionMetadataAsync(
            Erc20Transaction tx,
            CancellationToken cancellationToken = default)
        {
            var result = new Erc20TransactionMetadata()
            {
                Id = tx.Id,
                TransfersTypes = new List<TransactionType>()
            };

            foreach (var t in tx.Transfers)
            {
                var transferType = TransactionType.Unknown;

                var fromAddress = await GetAddressAsync(t.From, cancellationToken)
                    .ConfigureAwait(false);

                var isFromSelf = fromAddress != null;

                if (isFromSelf)
                {
                    transferType |= TransactionType.Output;
                    result.Amount -= t.Value;
                }

                var toAddress = await GetAddressAsync(t.To, cancellationToken)
                   .ConfigureAwait(false);

                var isToSelf = toAddress != null;

                if (isToSelf)
                {
                    transferType |= TransactionType.Input;
                    result.Amount += t.Value;
                }

                result.TransfersTypes.Add(transferType);
            }

            return result;
        }

        #endregion Transactions
    }
}