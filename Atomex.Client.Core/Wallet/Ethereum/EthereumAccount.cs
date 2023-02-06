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
using Atomex.Blockchain.Ethereum.Erc20.Messages;
using Atomex.Blockchain.Ethereum.Messages.Swaps.V1;
using Atomex.Common;
using Atomex.Core;
using Atomex.EthereumTokens;
using Atomex.Wallet.Abstract;
using Atomex.Wallets.Bips;

namespace Atomex.Wallet.Ethereum
{
    public class EthereumAccount : CurrencyAccount, IEstimatable, IHasTokens
    {
        private static ResourceLocker<string> _addressLocker;
        public static ResourceLocker<string> AddressLocker
        {
            get
            {
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
            ILocalStorage dataRepository)
                : base(currency, currencies, wallet, dataRepository)
        {
        }

        #region Common

        public EthereumConfig EthConfig => Currencies.Get<EthereumConfig>(Currency);
        private Erc20Config Erc20Config => Currencies.Get<Erc20Config>("USDT");

        public async Task<Result<string>> SendAsync(
            string from,
            string to,
            BigInteger amount,
            long gasLimit,
            decimal maxFeePerGas,
            decimal maxPriorityFeePerGas,
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
                gasLimit = GasLimitByType(TransactionType.Output);

                var (gasPrice, gasPriceError) = await ethConfig
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

            if (gasLimit < ethConfig.GasLimit)
                return new Error(
                    code: Errors.InsufficientGas,
                    message: "Insufficient gas");

            Log.Debug("Try to send {@amount} ETH with fee {@fee} from address {@address} with available balance {@balance}",
                addressFeeUsage.UsedAmount,
                addressFeeUsage.UsedFee,
                addressFeeUsage.WalletAddress.Address,
                addressFeeUsage.WalletAddress.AvailableBalance());

            // lock address to prevent nonce races
            using var addressLock = await AddressLocker
                .GetLockAsync(addressFeeUsage.WalletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            var api = ethConfig.GetEtherScanApi();

            var (nonce, nonceError) = await EthereumNonceManager.Instance
                .GetNonceAsync(api, addressFeeUsage.WalletAddress.Address, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (nonceError != null)
                return nonceError.Value;
            
            var txRequest = new EthereumTransactionRequest
            {
                From                 = addressFeeUsage.WalletAddress.Address,
                To                   = to.ToLowerInvariant(),
                Amount               = addressFeeUsage.UsedAmount,
                Nonce                = nonce,
                MaxFeePerGas         = EthereumHelper.GweiToWei(maxFeePerGas),
                MaxPriorityFeePerGas = EthereumHelper.GweiToWei(maxPriorityFeePerGas),
                GasLimit             = new BigInteger(gasLimit),
                ChainId              = ethConfig.ChainId,
                Data                 = null,
            };

            var signResult = await SignAsync(txRequest, cancellationToken)
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
                .BroadcastAsync(txRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (broadcastError != null)
                return broadcastError.Value;

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

        public async Task<bool> SignAsync(
            EthereumTransactionRequest txRequest,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var walletAddress = await GetAddressAsync(
                        address: txRequest.From,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                txRequest.Signature = await Wallet
                    .SignHashAsync(txRequest.GetRawHash(), walletAddress, EthConfig, cancellationToken)
                    .ConfigureAwait(false);

                return true;
            }
            catch (Exception e)
            {
                Log.Error(e, "[EthereumAccount] Sign error");
                return false;
            }
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

            var eth = EthConfig;

            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return new MaxAmountEstimation {
                    Error = new Error(Errors.AddressNotFound, Resources.AddressNotFoundInLocalDb)
                };

            var (estimatedGasPrice, estimateError) = await eth
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (estimateError != null)
                return new MaxAmountEstimation  {
                    Error = estimateError
                };

            var feeInWei = gasLimit == null
                ? GasLimitByType(type)
                : gasLimit.Value * EthereumHelper.GweiToWei(maxFeePerGas == null ? estimatedGasPrice.MaxFeePerGas : maxFeePerGas.Value);

            if (feeInWei == 0)
                return new MaxAmountEstimation {
                    Error = new Error(Errors.InsufficientFee, Resources.TooLowFees)
                };

            var reserveFeeInWei = ReserveFeeInWei(estimatedGasPrice.MaxFeePerGas);

            var requiredFeeInWei = feeInWei + (reserve ? reserveFeeInWei : 0);

            var restAmountInWei = fromAddress.AvailableBalance() - requiredFeeInWei;

            if (restAmountInWei < 0)
                return new MaxAmountEstimation {
                    Amount = restAmountInWei,
                    Fee = requiredFeeInWei,
                    Reserved = reserveFeeInWei,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFundsToCoverFees),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsToCoverFeesDetails,
                        requiredFeeInWei,
                        Currency,
                        fromAddress.AvailableBalance())
                };

            return new MaxAmountEstimation
            {
                Amount = restAmountInWei,
                Fee = feeInWei,
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
            var eth = EthConfig;

            if (type.HasFlag(TransactionType.SwapPayment))
                return eth.InitiateWithRewardGasLimit;

            if (type.HasFlag(TransactionType.SwapRefund))
                return eth.RefundGasLimit;

            if (type.HasFlag(TransactionType.SwapRedeem))
                return eth.RedeemGasLimit;

            return eth.GasLimit;
        }

        private BigInteger ReserveFeeInWei(decimal gasPrice)
        {
            var ethConfig = EthConfig;
            var erc20Config = Erc20Config;

            var maxGasLimit = Math.Max(Math.Max(erc20Config.RefundGasLimit, erc20Config.RedeemGasLimit), Math.Max(ethConfig.RefundGasLimit, ethConfig.RedeemGasLimit));

            return maxGasLimit * EthereumHelper.GweiToWei(gasPrice);
        }

        #endregion Common

        #region Balances

        public override async Task UpdateBalanceAsync(
             CancellationToken cancellationToken = default)
        {
            var scanner = new EthereumWalletScanner(this);

            await scanner
                .UpdateBalanceAsync(skipUsed: false, cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var scanner = new EthereumWalletScanner(this);

            await scanner
                .UpdateBalanceAsync(address, cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion Balances

        #region Addresses

        public async Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = await LocalStorage
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return unspentAddresses.MaxBy(w => w.AvailableBalance());

            var keyType = CurrencyConfig.StandardKey;

            foreach (var chain in new[] { Bip44.Internal, Bip44.External })
            {
                var keyPathPattern = EthConfig
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

        public async Task<IEnumerable<WalletAddress>> GetUnspentTokenAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            var result = new List<WalletAddress>();

            foreach (var token in Atomex.Currencies.EthTokens)
            {
                var addresses = await LocalStorage
                    .GetUnspentAddressesAsync(token)
                    .ConfigureAwait(false);

                result.AddRange(addresses);
            }

            return result;
        }

        private async Task<SelectedWalletAddress> CalculateFundsUsageAsync(
            string from,
            BigInteger amount,
            long gasLimit,
            decimal gasPrice,
            CancellationToken cancellationToken = default)
        {
            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return null; // invalid address

            var feeInWei = gasLimit * EthereumHelper.GweiToWei(gasPrice);

            var restBalanceInWei = fromAddress.AvailableBalance() -
               amount -
               feeInWei;

            if (restBalanceInWei < 0)
                return null; // insufficient funds

            return new SelectedWalletAddress
            {
                WalletAddress = fromAddress,
                UsedAmount    = amount,
                UsedFee       = feeInWei
            };
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

            foreach (var tx in txs.Cast<EthereumTransaction>())
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

        public async Task<EthereumTransactionMetadata> ResolveTransactionMetadataAsync(
            EthereumTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var result = new EthereumTransactionMetadata { Id = tx.Id };

            var fromAddress = await GetAddressAsync(tx.From, cancellationToken)
                .ConfigureAwait(false);

            var isFromSelf = fromAddress != null;

            if (isFromSelf)
            {
                result.Type |= TransactionType.Output;
                result.Amount -= tx.Amount + tx.GasUsed * tx.GasPrice;
            }

            var toAddress = await GetAddressAsync(tx.To, cancellationToken)
               .ConfigureAwait(false);

            var isToSelf = toAddress != null;

            if (isToSelf)
            {
                result.Type |= TransactionType.Input;
                result.Amount += tx.Amount;
            }

            if (tx.Data == null)
                return result;

            result.Type |= TransactionType.ContractCall;

            if (tx.IsMethodCall(FunctionSignatureExtractor.GetSignatureHash<InitiateMessage>()))
                result.Type |= TransactionType.SwapPayment;
            else if (tx.IsMethodCall(FunctionSignatureExtractor.GetSignatureHash<RedeemMessage>()))
                result.Type |= TransactionType.SwapRedeem;
            else if (tx.IsMethodCall(FunctionSignatureExtractor.GetSignatureHash<RefundMessage>()))
                result.Type |= TransactionType.SwapRefund;
            else if (tx.IsMethodCall(FunctionSignatureExtractor.GetSignatureHash<Erc20TransferMessage>()))
                result.Type |= TransactionType.TokenTransfer;
            else if (tx.IsMethodCall(FunctionSignatureExtractor.GetSignatureHash<Erc20ApproveMessage>()))
                result.Type |= TransactionType.TokenApprove;

            return result;
        }

        #endregion Transactions
    }
}