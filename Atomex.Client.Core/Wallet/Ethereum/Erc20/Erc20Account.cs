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
using Atomex.Blockchain;
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
    public class Erc20Account : ICurrencyAccount, IEstimatable
    {
        protected readonly string _tokenContract;
        protected readonly EthereumAccount _ethereumAccount;

        public ICurrencies Currencies { get; }
        public IHdWallet Wallet { get; }
        public ILocalStorage LocalStorage { get; }

        public Erc20Config Erc20Config => Currencies.FirstOrDefault(c => c is Erc20Config erc20 && erc20.TokenContractAddress == _tokenContract) as Erc20Config;
        private EthereumConfig EthConfig => Currencies.Get<EthereumConfig>(EthereumHelper.Eth);

        public Erc20Account(
            string tokenContract,
            ICurrencies currencies,
            IHdWallet wallet,
            ILocalStorage localStorage,
            EthereumAccount ethereumAccount)
        {
            Currencies   = currencies ?? throw new ArgumentNullException(nameof(currencies));
            Wallet       = wallet ?? throw new ArgumentNullException(nameof(wallet));
            LocalStorage = localStorage ?? throw new ArgumentNullException(nameof(localStorage));

            _tokenContract = tokenContract ?? throw new ArgumentNullException(nameof(tokenContract));
            _ethereumAccount = ethereumAccount ?? throw new ArgumentNullException(nameof(ethereumAccount));
        }

        #region Common

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

            var erc20 = Erc20Config;

            if (useDefaultFee)
            {
                gasLimit = GasLimitByType(TransactionType.Output);

                var (gasPrice, gasPriceError) = await erc20
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

            if (gasLimit < erc20.TransferGasLimit)
                return new Error(
                    code: Errors.InsufficientGas,
                    message: "Insufficient gas");

            var feeAmountInWei = gasLimit * maxFeePerGas.GweiToWei();

            Log.Debug("Fee per transaction {@feePerTransaction}. Fee Amount {@feeAmount}",
                gasLimit,
                feeAmountInWei);

            Log.Debug("Send {@amount} of {@currency} from address {@address} with available balance {@balance}",
                addressFeeUsage.UsedAmount,
                erc20.Name,
                addressFeeUsage.WalletAddress.Address,
                addressFeeUsage.WalletAddress.Balance);

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
                MaxFeePerGas         = maxFeePerGas.GweiToWei(),
                MaxPriorityFeePerGas = maxPriorityFeePerGas.GweiToWei(),
                Nonce                = nonce,
                TransactionType      = EthereumHelper.Eip1559TransactionType
            };

            txInput = message.CreateTransactionInput(_tokenContract);

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

            var erc20Config = Erc20Config;

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
                        erc20Config.Name) // currency code
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
                           (maxFeePerGas == null ? estimatedGasPrice.MaxFeePerGas.GweiToWei() : maxFeePerGas.Value.GweiToWei());

            if (feeInWei == 0)
                return new MaxAmountEstimation {
                    Error = new Error(Errors.InsufficientFee, Resources.TooLowFees)
                };

            var requiredFeeInWei = feeInWei + (reserve ? reserveFeeInWei : 0);

            var ethAddress = await LocalStorage
                .GetAddressAsync(eth.Name, tokenAddress.Address)
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
                        erc20Config.FeeCurrencyName, // currency code
                        0m)                          // available
                };

            var restBalanceInWei = ethAddress.Balance - requiredFeeInWei;

            if (restBalanceInWei < 0)
                return new MaxAmountEstimation {
                    Fee = requiredFeeInWei,
                    Reserved = reserveFeeInWei,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFundsToCoverFees),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsToCoverFeesDetails,
                        requiredFeeInWei,            // required fee
                        erc20Config.FeeCurrencyName, // currency code
                        ethAddress.Balance)          // available
                };

            if (tokenAddress.Balance <= 0)
                return new MaxAmountEstimation {
                    Fee = requiredFeeInWei,
                    Reserved = reserveFeeInWei,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFunds),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsDetails,
                        tokenAddress.Balance, // available tokens
                        erc20Config.Name)     // currency code
                };

            return new MaxAmountEstimation
            {
                Amount = tokenAddress.Balance,
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

            return maxGasLimit * gasPrice.GweiToWei();
        }

        #endregion Common

        #region Balances

        public async Task<Balance> GetAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await LocalStorage
                .GetAddressAsync(
                    currency: EthereumHelper.Erc20,
                    address: address,
                    tokenContract: _tokenContract,
                    tokenId: 0,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return walletAddress != null
                ? new Balance(
                    walletAddress.Balance,
                    walletAddress.UnconfirmedIncome,
                    walletAddress.UnconfirmedOutcome)
                : new Balance();
        }

        public virtual async Task<Balance> GetBalanceAsync()
        {
            BigInteger balance = 0;

            var addresses = await LocalStorage
                .GetUnspentAddressesAsync(
                    currency: EthereumHelper.Erc20,
                    tokenContract: _tokenContract,
                    tokenId: 0)
                .ConfigureAwait(false);

            foreach (var address in addresses)
                balance += address.Balance;

            return new Balance(balance, unconfirmedIncome: 0, unconfirmedOutcome: 0);
        }

        public async Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            var scanner = new Erc20WalletScanner(this, _ethereumAccount);

            await scanner
                .UpdateBalanceAsync(skipUsed: false, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task UpdateBalanceAsync(
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

        public virtual Task<WalletAddress> DivideAddressAsync(
            string keyPath,
            int keyType)
        {
            var walletAddress = Wallet.GetAddress(
                currency: EthConfig,
                keyPath: keyPath,
                keyType: keyType);

            if (walletAddress == null)
                return null;

            var erc20Config = Erc20Config;

            walletAddress.Currency = EthereumHelper.Erc20;

            walletAddress.TokenBalance = new TokenBalance
            {
                Address     = walletAddress.Address,
                Contract    = _tokenContract,
                TokenId     = 0,
                Symbol      = erc20Config.Name,
                Standard    = EthereumHelper.Erc20,
                Decimals    = erc20Config.Decimals,
                Description = erc20Config.Description,
                Balance     = "0"
            };

            return Task.FromResult(walletAddress);
        }

        public Task<WalletAddress> GetAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return LocalStorage
                .GetAddressAsync(
                    currency: EthereumHelper.Erc20,
                    address: address,
                    tokenContract: _tokenContract,
                    tokenId: 0,
                    cancellationToken: cancellationToken);
        }

        public Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return LocalStorage
                .GetAddressesAsync(
                    currency: EthereumHelper.Erc20,
                    tokenContract: _tokenContract,
                    cancellationToken: cancellationToken);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return LocalStorage
                .GetUnspentAddressesAsync(
                    currency: EthereumHelper.Erc20,
                    tokenContract: _tokenContract,
                    tokenId: 0,
                    cancellationToken: cancellationToken);
        }

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

            var feeInWei = gasLimit * gasPrice.GweiToWei();

            var ethAddress = await LocalStorage
                .GetAddressAsync(eth.Name, fromAddress.Address)
                .ConfigureAwait(false);

            var availableBalanceInWei = ethAddress?.Balance ?? 0;

            if (availableBalanceInWei < feeInWei)
            {
                Log.Debug("Unsufficient ETH ammount for ERC20 token processing on address {@address} with available balance {@balance} and needed amount {@amount}",
                    ethAddress.Address,
                    availableBalanceInWei,
                    feeInWei);

                return null; // insufficient funds
            }

            var restBalanceInTokenDigits = fromAddress.Balance - amount;

            if (restBalanceInTokenDigits < 0) // todo: log?
                return null;

            return new SelectedWalletAddress
            {
                WalletAddress = fromAddress,
                UsedAmount = amount,
                UsedFee = feeInWei
            };
        }

        public async Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            // addresses with tokens
            var unspentAddresses = await LocalStorage
                .GetUnspentAddressesAsync(
                    currency: EthereumHelper.Erc20,
                    tokenContract: _tokenContract,
                    tokenId: 0,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return unspentAddresses.MaxBy(a => a.Balance);

            // addresses with eth
            var unspentEthereumAddresses = await LocalStorage
                .GetUnspentAddressesAsync(EthereumHelper.Eth)
                .ConfigureAwait(false);

            if (unspentEthereumAddresses.Any())
            {
                var ethereumAddress = unspentEthereumAddresses.MaxBy(a => a.Balance);

                var result = await DivideAddressAsync(
                        keyPath: ethereumAddress.KeyPath,
                        keyType: ethereumAddress.KeyType)
                    .ConfigureAwait(false);

                _ = await LocalStorage
                    .UpsertAddressAsync(result, cancellationToken)
                    .ConfigureAwait(false);

                return result;
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

            var freeAddress = await DivideAddressAsync(
                    keyPath: keyPath,
                    keyType: keyType)
                .ConfigureAwait(false);

            _ = await LocalStorage
                .UpsertAddressAsync(freeAddress, cancellationToken)
                .ConfigureAwait(false);

            return freeAddress;
        }

        #endregion Addresses

        #region Transactions

        public Task<IEnumerable<ITransaction>> GetUnconfirmedTransactionsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Enumerable.Empty<ITransaction>()); // all transfers are always confirmed
        }

        public async Task ResolveTransactionsMetadataAsync(
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

        public async Task<ITransactionMetadata> ResolveTransactionMetadataAsync(
            ITransaction tx,
            CancellationToken cancellationToken = default)
        {
            return await ResolveTransactionMetadataAsync(
                    (Erc20Transaction)tx,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<TransactionMetadata> ResolveTransactionMetadataAsync(
            Erc20Transaction tx,
            CancellationToken cancellationToken = default)
        {
            var result = new TransactionMetadata()
            {
                Id = tx.Id,
                Currency = tx.Currency,
                Internals = new List<InternalTransactionMetadata>()
            };

            foreach (var t in tx.Transfers)
            {
                var transferType = TransactionType.Unknown;
                var transferAmount = BigInteger.Zero;

                var fromAddress = await GetAddressAsync(t.From, cancellationToken)
                    .ConfigureAwait(false);

                var isFromSelf = fromAddress != null;

                if (isFromSelf)
                {
                    transferType |= TransactionType.Output;
                    transferAmount -= t.Value;
                }

                var toAddress = await GetAddressAsync(t.To, cancellationToken)
                   .ConfigureAwait(false);

                var isToSelf = toAddress != null;

                if (isToSelf)
                {
                    transferType |= TransactionType.Input;
                    transferAmount += t.Value;
                }

                result.Internals.Add(new InternalTransactionMetadata
                {
                    Type = transferType,
                    Amount = transferAmount
                });

                result.Type |= transferType;
                result.Amount += transferAmount;
            }

            return result;
        }

        #endregion Transactions
    }
}