#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Serilog;
using Nethereum.Signer.Crypto;

using Atomex.Abstract;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Erc20.Messages;
using Atomex.Blockchain.Ethereum.Messages.Swaps.V1;
using Atomex.Common;
using Atomex.EthereumTokens;
using Atomex.Wallet.Abstract;
using Atomex.Wallets;
using Atomex.Wallets.Bips;
using Atomex.Wallets.Abstract;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Atomex.Wallet.Ethereum
{
    public class EthereumAccount : CurrencyAccount, IEstimatable, IHasTokens
    {
        private static ResourceLocker<string>? _addressLocker;
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
            ILocalStorage localStorage)
                : base(currency, currencies, wallet, localStorage)
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

                if (gasPrice == null)
                    return new Error(Errors.GetGasPriceError, "Null gas price received");

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
                addressFeeUsage.WalletAddress.Balance);

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
                Value               = addressFeeUsage.UsedAmount,
                Nonce                = nonce,
                MaxFeePerGas         = maxFeePerGas.GweiToWei(),
                MaxPriorityFeePerGas = maxPriorityFeePerGas.GweiToWei(),
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

            var tx = new EthereumTransaction(txRequest, txId);

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

                var rawHash = txRequest.GetRawHash();

                txRequest.Signature = await Wallet
                    .SignHashAsync(rawHash, walletAddress, EthConfig, cancellationToken)
                    .ConfigureAwait(false);

                var publicKey = Wallet.GetPublicKey(
                    EthConfig,
                    walletAddress.KeyPath,
                    walletAddress.KeyType);

                // manually calculate and set V signature field, because DER signature contains only R and S numbers
                // the same as EthECKey.SignAndCalculateYParityV result
                txRequest.SignatureV = new byte[]
                {
                    (byte)EthereumTransactionRequest.CalculateRecId(
                        signature: new ECDSASignature(txRequest.Signature),
                        hash: rawHash,
                        uncompressedPublicKey: publicKey)
                };

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

            if (gasPrice == null)
                return new Error(Errors.GetGasPriceError, "Null gas price received");

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
            string? from,
            TransactionType type,
            long? gasLimit,
            decimal? maxFeePerGas,
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(from))
            {
                return new EthereumMaxAmountEstimation {
                    Error = new Error(Errors.FromAddressIsNullOrEmpty, Resources.FromAddressIsNullOrEmpty)
                };
            }

            //if (from == to)
            //    return new MaxAmountEstimation {
            //        Error = new Error(Errors.SendingAndReceivingAddressesAreSame, "Sending and receiving addresses are same")
            //    };

            var eth = EthConfig;

            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
            {
                return new EthereumMaxAmountEstimation {
                    Error = new Error(Errors.AddressNotFound, Resources.AddressNotFoundInLocalDb)
                };
            }

            var (estimatedGasPrice, estimateError) = await eth
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (estimateError != null)
                return new EthereumMaxAmountEstimation { Error = estimateError };

            if (estimatedGasPrice == null)
                return new EthereumMaxAmountEstimation { Error = new Error(Errors.GetGasPriceError, "Null gas price received") };

            var gasLimitValue = gasLimit == null
                ? GasLimitByType(type)
                : gasLimit.Value;

            var maxFeePerGasValue = maxFeePerGas == null
                ? estimatedGasPrice.MaxFeePerGas.GweiToWei()
                : maxFeePerGas.Value.GweiToWei();

            var feeInWei = gasLimitValue * maxFeePerGasValue;

            if (feeInWei == 0)
            {
                return new EthereumMaxAmountEstimation
                {
                    GasPrice = estimatedGasPrice,
                    Error = new Error(Errors.InsufficientFee, Resources.TooLowFees),
                };
            }

            var reserveFeeInWei = ReserveFeeInWei(estimatedGasPrice.MaxFeePerGas);

            var requiredFeeInWei = feeInWei + (reserve ? reserveFeeInWei : 0);

            var restAmountInWei = fromAddress.Balance - requiredFeeInWei;

            if (restAmountInWei < 0)
            {
                return new EthereumMaxAmountEstimation
                {
                    Amount = restAmountInWei,
                    Fee = requiredFeeInWei,
                    Reserved = reserveFeeInWei,
                    GasPrice = estimatedGasPrice,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFundsToCoverFees),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsToCoverFeesDetails,
                        requiredFeeInWei,
                        Currency,
                        fromAddress.Balance)
                };
            }

            return new EthereumMaxAmountEstimation
            {
                Amount = restAmountInWei,
                Fee = feeInWei,
                Reserved = reserveFeeInWei,
                GasPrice = estimatedGasPrice
            };
        }

        public Task<MaxAmountEstimation> EstimateMaxSwapPaymentAmountAsync(
            IFromSource fromSource,
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            var fromAddress = (fromSource as FromAddress)?.Address;

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

            return maxGasLimit * gasPrice.GweiToWei();
        }

        #endregion Common

        #region Balances

        public override async Task UpdateBalanceAsync(
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            var scanner = new EthereumWalletScanner(this);

            await scanner
                .UpdateBalanceAsync(skipUsed: false, cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task UpdateBalanceAsync(
            string address,
            ILogger? logger = null,
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
                return unspentAddresses.MaxBy(w => w.Balance);

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

            foreach (var tokenStandard in Atomex.Currencies.EthTokensStandards)
            {
                var addresses = await LocalStorage
                    .GetUnspentAddressesAsync(tokenStandard)
                    .ConfigureAwait(false);

                result.AddRange(addresses);
            }

            return result;
        }

        private async Task<SelectedWalletAddress?> CalculateFundsUsageAsync(
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

            var feeInWei = gasLimit * gasPrice.GweiToWei();

            var restBalanceInWei = fromAddress.Balance - amount - feeInWei;

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

        public async Task<TransactionMetadata> ResolveTransactionMetadataAsync(
            EthereumTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var result = new TransactionMetadata
            {
                Id = tx.Id,
                Currency = tx.Currency,
                Internals = new List<InternalTransactionMetadata>()
            };

            var metadata = await ResolveTransactionMetadataAsync(tx, tx.GasPrice, cancellationToken)
                .ConfigureAwait(false);

            result.Type = metadata.Type;
            result.Amount = metadata.Amount;
            result.Fee = metadata.Fee;

            if (tx.InternalTransactions != null && tx.InternalTransactions.Any())
            {
                foreach (var internalTx in tx.InternalTransactions)
                {
                    var internalMetadata = await ResolveTransactionMetadataAsync(internalTx, tx.GasPrice, cancellationToken)
                        .ConfigureAwait(false);

                    result.Internals.Add(internalMetadata);
                }
            }

            return result;
        }

        public override async Task<ITransactionMetadata> ResolveTransactionMetadataAsync(
            ITransaction tx,
            CancellationToken cancellationToken = default)
        {
            return await ResolveTransactionMetadataAsync(
                    (EthereumTransaction)tx,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<InternalTransactionMetadata> ResolveTransactionMetadataAsync(
            IEthereumTransaction tx,
            BigInteger gasPrice,
            CancellationToken cancellationToken = default)
        {
            var result = new InternalTransactionMetadata();

            var fromAddress = await GetAddressAsync(tx.From, cancellationToken)
                .ConfigureAwait(false);

            var isFromSelf = fromAddress != null;

            if (isFromSelf)
            {
                result.Type |= TransactionType.Output;
                result.Amount -= tx.Value;
                result.Fee += tx.GasUsed * gasPrice;
            }

            var toAddress = await GetAddressAsync(tx.To, cancellationToken)
               .ConfigureAwait(false);

            var isToSelf = toAddress != null;

            if (isToSelf)
            {
                result.Type |= TransactionType.Input;
                result.Amount += tx.Value;
            }

            if (tx.Data != null && tx.Data != "0x")
            {
                result.Type |= TransactionType.ContractCall;

                if (tx.Data.IsMethodCall(FunctionSignatureExtractor.GetSignatureHash<InitiateMessage>()))
                    result.Type |= TransactionType.SwapPayment;
                else if (tx.Data.IsMethodCall(FunctionSignatureExtractor.GetSignatureHash<RedeemMessage>()))
                    result.Type |= TransactionType.SwapRedeem;
                else if (tx.Data.IsMethodCall(FunctionSignatureExtractor.GetSignatureHash<RefundMessage>()))
                    result.Type |= TransactionType.SwapRefund;
                else if (tx.Data.IsMethodCall(FunctionSignatureExtractor.GetSignatureHash<Erc20TransferMessage>()))
                    result.Type |= TransactionType.TokenTransfer;
                else if (tx.Data.IsMethodCall(FunctionSignatureExtractor.GetSignatureHash<Erc20ApproveMessage>()))
                    result.Type |= TransactionType.TokenApprove;
            }

            return result;
        }

        #endregion Transactions
    }
}