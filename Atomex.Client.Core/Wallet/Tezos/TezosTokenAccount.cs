using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.TezosTokens;
using Atomex.Wallet.Abstract;
using Atomex.Wallets;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallet.Tezos
{
    public abstract class TezosTokenAccount : ICurrencyAccount, IEstimatable
    {
        protected readonly string _tokenContract;
        protected readonly BigInteger _tokenId;
        protected readonly TezosAccount _tezosAccount;

        public string Currency { get; }
        public string TokenType { get; }
        public ICurrencies Currencies { get; }
        public IHdWallet Wallet { get; }
        public ILocalStorage LocalStorage { get; }

        protected TezosConfig XtzConfig => Currencies.Get<TezosConfig>(TezosConfig.Xtz);
        protected TezosTokenConfig TokenConfig => Currencies.Get<TezosTokenConfig>(Currency);

        public TezosTokenAccount(
            string tokenType,
            string tokenContract,
            BigInteger tokenId,
            ICurrencies currencies,
            IHdWallet wallet,
            ILocalStorage localStorage,
            TezosAccount tezosAccount)
        {
            TokenType    = tokenType ?? throw new ArgumentNullException(nameof(tokenType));
            Currencies   = currencies ?? throw new ArgumentNullException(nameof(currencies));
            Wallet       = wallet ?? throw new ArgumentNullException(nameof(wallet));
            LocalStorage = localStorage ?? throw new ArgumentNullException(nameof(localStorage));

            _tokenContract = tokenContract ?? throw new ArgumentNullException(nameof(tokenContract));
            _tokenId       = tokenId;
            _tezosAccount  = tezosAccount ?? throw new ArgumentNullException(nameof(tezosAccount));

            Currency = Currencies
                .Where(c => c is TezosTokenConfig token && token.TokenContractAddress == tokenContract && token.TokenId == tokenId)
                .FirstOrDefault()?.Name ?? tokenType;
        }

        public async Task<Result<string>> SendAsync(
            string from,
            string to,
            BigInteger amount,
            long fee,
            bool useDefaultFee = true,
            CancellationToken cancellationToken = default)
        {
            var tokenConfig = TokenConfig;

            var addressFeeUsage = await CalculateFundsUsageAsync(
                    from: from,
                    amount: amount,
                    fee: fee,
                    feeUsagePolicy: useDefaultFee
                        ? FeeUsagePolicy.EstimatedFee
                        : FeeUsagePolicy.FeePerTransaction,
                    transactionType: TransactionType.Output,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (addressFeeUsage == null)
                return new Error(
                    code: Errors.InsufficientFunds,
                    message: "Insufficient funds");

            var addressAmountInTokenDigits = addressFeeUsage.UsedAmount;

            Log.Debug("Send {@amount} tokens from address {@address} with available balance {@balance}",
                addressAmountInTokenDigits,
                addressFeeUsage.WalletAddress.Address,
                addressFeeUsage.WalletAddress.Balance);

            var (result, error) = await _tezosAccount
                .SendTransactionAsync(
                    from: from,
                    to: _tokenContract,
                    amount: 0,
                    fee: useDefaultFee
                        ? Fee.FromNetwork((long)addressFeeUsage.UsedFee)
                        : Fee.FromValue((long)addressFeeUsage.UsedFee),
                    gasLimit: useDefaultFee
                        ? GasLimit.FromNetwork((int)tokenConfig.TransferGasLimit)
                        : GasLimit.FromValue((int)tokenConfig.TransferGasLimit),
                    storageLimit: useDefaultFee
                        ? StorageLimit.FromNetwork((int)tokenConfig.TransferStorageLimit, useSafeValue: false)
                        : StorageLimit.FromValue((int)tokenConfig.TransferStorageLimit),
                    entrypoint: "transfer",
                    parameters: CreateTransferParams(from, to, addressAmountInTokenDigits),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            return result.OperationId;
        }

        public async Task<long> EstimateFeeAsync(
            string from,
            TransactionType type,
            CancellationToken cancellationToken = default)
        {
            var txFeeInMtz = await FeeInMtzByType(
                    type: type,
                    from: from,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInMtz = StorageFeeInMtzByType(type);

            return txFeeInMtz + storageFeeInMtz;
        }

        public async Task<Result<decimal>> EstimateSwapPaymentFeeAsync(
            IFromSource from,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            var fromAddress = (from as FromAddress)?.Address;

            var feeInMtz = await EstimateFeeAsync(
                    from: fromAddress,
                    type: TransactionType.SwapPayment,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return feeInMtz.ToTez();
        }

        public virtual async Task<(long fee, bool isEnougth)> EstimateTransferFeeAsync(
            string from,
            CancellationToken cancellationToken = default)
        {
            var xtzAddress = await _tezosAccount
                .GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var txFeeInMtz = await FeeInMtzByType(
                    type: TransactionType.Output,
                    from: xtzAddress?.Address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInMtz = StorageFeeInMtzByType(
                type: TransactionType.Output);

            var requiredFeeInMtz = txFeeInMtz + storageFeeInMtz + XtzConfig.MicroTezReserve;

            var availableBalanceInMtz = xtzAddress != null
                ? xtzAddress.Balance
                : 0;

            return (
                fee: requiredFeeInMtz,
                isEnougth: availableBalanceInMtz >= requiredFeeInMtz);
        }

        public async Task<MaxAmountEstimation> EstimateMaxAmountToSendAsync(
            string from,
            TransactionType type,
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(from))
                return new MaxAmountEstimation
                {
                    Error = new Error(Errors.FromAddressIsNullOrEmpty, Resources.FromAddressIsNullOrEmpty)
                };

            //if (from == to)
            //    return new MaxAmountEstimation {
            //        Error = new Error(Errors.SendingAndReceivingAddressesAreSame, "Sending and receiving addresses are same")
            //    };

            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return new MaxAmountEstimation
                {
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFunds),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsDetails,
                        0,                // available tokens
                        TokenConfig.Name) // currency code
                };

            var reserveFeeInMtz = ReserveFeeInMtz();

            var xtz = XtzConfig;

            var feeInMtz = await FeeInMtzByType(
                    type: type,
                    from: fromAddress.Address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInMtz = StorageFeeInMtzByType(type);

            var requiredFeeInMtz = feeInMtz +
                storageFeeInMtz +
                (reserve ? reserveFeeInMtz : 0) +
                xtz.MicroTezReserve;

            var xtzAddress = await LocalStorage
                .GetAddressAsync(xtz.Name, fromAddress.Address)
                .ConfigureAwait(false);

            if (xtzAddress == null)
                return new MaxAmountEstimation
                {
                    Fee = requiredFeeInMtz,
                    Reserved = reserveFeeInMtz,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFundsToCoverFees),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsToCoverFeesDetails,
                        requiredFeeInMtz,            // required fee
                        TokenConfig.FeeCurrencyName, // currency code
                        0m)                          // available
                };

            var restBalanceInMtz = xtzAddress.Balance - requiredFeeInMtz;
                
            if (restBalanceInMtz < 0)
                return new MaxAmountEstimation
                {
                    Fee = requiredFeeInMtz,
                    Reserved = reserveFeeInMtz,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFundsToCoverFees),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsToCoverFeesDetails,
                        requiredFeeInMtz,            // required fee
                        TokenConfig.FeeCurrencyName, // currency code
                        xtzAddress.Balance)          // available
                };

            if (fromAddress.Balance <= 0)
                return new MaxAmountEstimation
                {
                    Fee = requiredFeeInMtz,
                    Reserved = reserveFeeInMtz,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFunds),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsDetails,
                        fromAddress.Balance, // available tokens
                        TokenConfig.Name)    // currency code
                };

            return new MaxAmountEstimation
            {
                Amount   = fromAddress.Balance,
                Fee      = requiredFeeInMtz,
                Reserved = reserveFeeInMtz
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
                reserve: reserve,
                cancellationToken: cancellationToken);
        }

        private async Task<long> FeeInMtzByType(
            TransactionType type,
            string from,
            CancellationToken cancellationToken = default)
        {
            var tokenConfig = TokenConfig;

            var isRevealed = from != null && await _tezosAccount
                .IsRevealedSourceAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var revealFeeInMtz = !isRevealed
                ? tokenConfig.RevealFee
                : 0;

            if (type.HasFlag(TransactionType.TokenApprove))
                return tokenConfig.ApproveFee;

            if (type.HasFlag(TransactionType.SwapPayment))
                return tokenConfig.ApproveFee * 2 + tokenConfig.InitiateFee + revealFeeInMtz;

            if (type.HasFlag(TransactionType.SwapRefund))
                return tokenConfig.RefundFee + revealFeeInMtz;

            if (type.HasFlag(TransactionType.SwapRedeem))
                return tokenConfig.RedeemFee + revealFeeInMtz;

            return tokenConfig.TransferFee + revealFeeInMtz;
        }

        private long ReserveFeeInMtz()
        {
            var xtz = XtzConfig;
            var tokenConfig = TokenConfig;

            var tokenRedeemFeeInMtz = tokenConfig.RedeemFee + Math.Max(tokenConfig.RedeemStorageLimit * tokenConfig.StorageFeeMultiplier, 0);
            var tokenRefundFeeInMtz = tokenConfig.RefundFee + Math.Max(tokenConfig.RefundStorageLimit * tokenConfig.StorageFeeMultiplier, 0);
            var xtzRedeemFeeInMtz = xtz.RedeemFee + Math.Max(xtz.RedeemStorageLimit * xtz.StorageFeeMultiplier, 0);
            var xtzRefundFeeInMtz = xtz.RefundFee + Math.Max(xtz.RefundStorageLimit * xtz.StorageFeeMultiplier, 0);

            var maxTxFeeInMtz = new[]
            {
                tokenRedeemFeeInMtz,
                tokenRefundFeeInMtz,
                xtzRedeemFeeInMtz,
                xtzRefundFeeInMtz

            }.Max();
            
            return maxTxFeeInMtz + tokenConfig.RevealFee + XtzConfig.MicroTezReserve;
        }

        private long StorageFeeInMtzByType(TransactionType type)
        {
            var tokenConfig = TokenConfig;

            if (type.HasFlag(TransactionType.TokenApprove))
                return tokenConfig.ApproveStorageLimit * tokenConfig.StorageFeeMultiplier;

            if (type.HasFlag(TransactionType.SwapPayment))
                return (tokenConfig.ApproveStorageLimit + tokenConfig.InitiateStorageLimit) * tokenConfig.StorageFeeMultiplier;

            if (type.HasFlag(TransactionType.SwapRefund))
                return tokenConfig.RefundStorageLimit * tokenConfig.StorageFeeMultiplier;

            if (type.HasFlag(TransactionType.SwapRedeem))
                return tokenConfig.RedeemStorageLimit * tokenConfig.StorageFeeMultiplier;

            return tokenConfig.TransferStorageLimit * tokenConfig.StorageFeeMultiplier;
        }

        protected async Task<SelectedWalletAddress> CalculateFundsUsageAsync(
            string from,
            BigInteger amount,
            long fee,
            FeeUsagePolicy feeUsagePolicy,
            TransactionType transactionType,
            CancellationToken cancellationToken = default)
        {
            var xtz = XtzConfig;

            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return null; // invalid address

            var xtzAddress = await LocalStorage
                .GetAddressAsync(xtz.Name, fromAddress.Address)
                .ConfigureAwait(false);

            var availableBalanceInMtz = xtzAddress?.Balance ?? 0;

            var txFeeInMtz = feeUsagePolicy == FeeUsagePolicy.EstimatedFee
                ? await FeeInMtzByType(
                        type: transactionType,
                        from: fromAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
                : fee;

            var storageFeeInMtz = StorageFeeInMtzByType(transactionType);

            var restBalanceInMtz = availableBalanceInMtz -
                txFeeInMtz -
                storageFeeInMtz -
                xtz.MicroTezReserve;

            if (restBalanceInMtz < 0)
            {
                Log.Debug("Unsufficient XTZ ammount for Tezos token processing on address {@address} with available balance {@balance} and needed amount {@amount}",
                    fromAddress.Address,
                    availableBalanceInMtz,
                    txFeeInMtz + storageFeeInMtz + xtz.MicroTezReserve);

                return null;
            }

            var restBalanceInTokenDigits = fromAddress.Balance - amount;

            if (restBalanceInTokenDigits < 0) // todo: log?
                return null;

            return new SelectedWalletAddress
            {
                WalletAddress  = fromAddress,
                UsedAmount     = amount,
                UsedFee        = txFeeInMtz,
                UsedStorageFee = storageFeeInMtz
            };
        }

        #region Balances

        public virtual async Task<Balance> GetAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await LocalStorage
                .GetAddressAsync(
                    currency: TokenType,
                    address: address,
                    tokenContract: _tokenContract,
                    tokenId: _tokenId,
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
                    currency: TokenType,
                    tokenContract: _tokenContract,
                    tokenId: _tokenId)
                .ConfigureAwait(false);

            foreach (var address in addresses)
                balance += address.Balance;

            return new Balance(balance, unconfirmedIncome: 0, unconfirmedOutcome: 0);
        }

        public async Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            var scanner = new TezosTokensWalletScanner(_tezosAccount, TokenType);

            await scanner
                .UpdateBalanceAsync(
                    tokenContract: _tokenContract,
                    tokenId: _tokenId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var scanner = new TezosTokensWalletScanner(_tezosAccount, TokenType);

            await scanner
                .UpdateBalanceAsync(
                    address: address,
                    tokenContract: _tokenContract,
                    tokenId: _tokenId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion Balances

        #region Addresses

        public Task<WalletAddress> DivideAddressAsync(
            string keyPath,
            int keyType)
        {
            var walletAddress = Wallet.GetAddress(
                currency: XtzConfig,
                keyPath: keyPath,
                keyType: keyType);

            if (walletAddress == null)
                return null;

            walletAddress.Currency = TokenType;

            var tokenConfig = Currencies
                .Where(c => c is TezosTokenConfig token && token.TokenContractAddress == _tokenContract && token.TokenId == _tokenId)
                .FirstOrDefault();

            walletAddress.TokenBalance = new TokenBalance
            {
                Address  = walletAddress.Address,
                Contract = _tokenContract,
                TokenId  = _tokenId,
                Symbol   = tokenConfig?.DisplayedName,
                Standard = TokenType switch
                {
                    TezosHelper.Fa12 => "fa1.2",
                    TezosHelper.Fa2 => "fa2",
                    _ => throw new NotSupportedException($"Not supported TokenType: {TokenType ?? "<null>"}")
                },
                Decimals = tokenConfig?.Decimals ?? 0
            };

            return Task.FromResult(walletAddress);
        }

        public Task<WalletAddress> GetAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return LocalStorage.GetAddressAsync(
                currency: TokenType,
                address: address,
                tokenContract: _tokenContract,
                tokenId: _tokenId,
                cancellationToken: cancellationToken);
        }

        public Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return LocalStorage.GetAddressesAsync(
                currency: TokenType,
                tokenContract: _tokenContract,
                cancellationToken: cancellationToken);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return LocalStorage.GetUnspentAddressesAsync(
                currency: TokenType,
                tokenContract: _tokenContract,
                tokenId: _tokenId,
                cancellationToken: cancellationToken);
        }

        public async Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            // 1. try to find address with tokens
            var unspentAddresses = await LocalStorage
                .GetUnspentAddressesAsync(
                    currency: TokenType,
                    tokenContract: _tokenContract,
                    tokenId: _tokenId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return unspentAddresses.MaxBy(w => w.Balance);

            // 2. try to find xtz address with max balance
            var unspentTezosAddresses = await LocalStorage
                .GetUnspentAddressesAsync(
                    currency: TezosConfig.Xtz,
                    includeUnconfirmed: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (unspentTezosAddresses.Any())
            {
                var xtzAddress = unspentTezosAddresses.MaxBy(a => a.Balance);

                var tokenAddress = await LocalStorage
                    .GetAddressAsync(
                        currency: TokenType,
                        address: xtzAddress.Address,
                        tokenContract: _tokenContract,
                        tokenId: _tokenId,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (tokenAddress != null)
                    return tokenAddress;

                return await DivideAddressAsync(
                        keyPath: xtzAddress.KeyPath,
                        keyType: xtzAddress.KeyType)
                    .ConfigureAwait(false);
            }

            // 3. use xtz redeem address
            var xtzRedeemAddress = await _tezosAccount
                .GetRedeemAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            var tokenRedeemAddress = await LocalStorage
                .GetAddressAsync(
                    currency: TokenType,
                    address: xtzRedeemAddress.Address,
                    tokenContract: _tokenContract,
                    tokenId: _tokenId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (tokenRedeemAddress != null)
                return tokenRedeemAddress;

            return await DivideAddressAsync(
                    keyPath: xtzRedeemAddress.KeyPath,
                    keyType: xtzRedeemAddress.KeyType)
                .ConfigureAwait(false);
        }

        public Task<WalletAddress> GetRedeemAddressAsync( // todo: match it with xtz balances
            CancellationToken cancellationToken = default)
        {
            return GetFreeExternalAddressAsync(cancellationToken);
        }

        #endregion Addresses

        #region Helpers

        protected abstract string CreateTransferParams(
            string from,
            string to,
            BigInteger amount);

        #endregion Helpers

        #region Transactions

        public Task<IEnumerable<ITransaction>> GetUnconfirmedTransactionsAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<ITransaction>());

        public async Task ResolveTransactionsMetadataAsync(
            IEnumerable<ITransaction> txs,
            CancellationToken cancellationToken = default)
        {
            var resolvedMetadata = new List<ITransactionMetadata>();

            foreach (var tx in txs.Cast<TezosTokenTransfer>())
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
                    (TezosTokenTransfer)tx,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<TransactionMetadata> ResolveTransactionMetadataAsync(
            TezosTokenTransfer tx,
            CancellationToken cancellationToken = default)
        {
            var result = new TransactionMetadata
            {
                Id = tx.Id,
                Currency = tx.Currency
            };

            var fromAddress = await GetAddressAsync(tx.From, cancellationToken)
                .ConfigureAwait(false);

            var isFromSelf = fromAddress != null;

            if (isFromSelf)
            {
                result.Type |= TransactionType.Output;
                result.Amount -= tx.GetAmount();
            }

            var toAddress = await GetAddressAsync(tx.To, cancellationToken)
               .ConfigureAwait(false);

            var isToSelf = toAddress != null;

            if (isToSelf)
            {
                result.Type |= TransactionType.Input;
                result.Amount += tx.GetAmount();
            }

            return result;
        }

        #endregion Transactions
    }
}