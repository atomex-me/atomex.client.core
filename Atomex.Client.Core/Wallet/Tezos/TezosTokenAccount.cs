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

namespace Atomex.Wallet.Tezos
{
    public abstract class TezosTokenAccount : ICurrencyAccount, IEstimatable
    {
        protected readonly string _tokenContract;
        protected readonly int _tokenId;
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
            int tokenId,
            ICurrencies currencies,
            IHdWallet wallet,
            ILocalStorage localStorage,
            TezosAccount tezosAccount)
        {
            TokenType      = tokenType ?? throw new ArgumentNullException(nameof(tokenType));
            Currencies     = currencies ?? throw new ArgumentNullException(nameof(currencies));
            Wallet         = wallet ?? throw new ArgumentNullException(nameof(wallet));
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
                addressFeeUsage.WalletAddress.AvailableBalance());

            var storageLimit = Math.Max(tokenConfig.TransferStorageLimit - tokenConfig.ActivationStorage, 0); // without activation storage fee

            var (result, error) = await _tezosAccount
                .SendTransactionAsync(
                    from: from,
                    to: _tokenContract,
                    amount: 0,
                    fee: Fee.FromValue((long)addressFeeUsage.UsedFee),
                    gasLimit: GasLimit.FromValue((int)tokenConfig.TransferGasLimit),
                    storageLimit: StorageLimit.FromValue((int)storageLimit),
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

        public async Task<decimal?> EstimateSwapPaymentFeeAsync(
            IFromSource from,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            var fromAddress = (from as FromAddress)?.Address;

            return await EstimateFeeAsync(
                    from: fromAddress,
                    type: TransactionType.SwapPayment,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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
                ? xtzAddress.AvailableBalance()
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
                .GetWalletAddressAsync(xtz.Name, fromAddress.Address)
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

            var restBalanceInMtz = xtzAddress.AvailableBalance() - requiredFeeInMtz;

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
                        requiredFeeInMtz,              // required fee
                        TokenConfig.FeeCurrencyName,   // currency code
                        xtzAddress.AvailableBalance()) // available
                };

            if (fromAddress.AvailableBalance() <= 0)
                return new MaxAmountEstimation
                {
                    Fee = requiredFeeInMtz,
                    Reserved = reserveFeeInMtz,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFunds),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsDetails,
                        fromAddress.AvailableBalance(), // available tokens
                        TokenConfig.Name)               // currency code
                };

            return new MaxAmountEstimation
            {
                Amount   = fromAddress.AvailableBalance(),
                Fee      = requiredFeeInMtz,
                Reserved = reserveFeeInMtz
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

            var tokenRedeemFeeInMtz = tokenConfig.RedeemFee + Math.Max((tokenConfig.RedeemStorageLimit - tokenConfig.ActivationStorage) * tokenConfig.StorageFeeMultiplier, 0);
            var tokenRefundFeeInMtz = tokenConfig.RefundFee + Math.Max((tokenConfig.RefundStorageLimit - tokenConfig.ActivationStorage) * tokenConfig.StorageFeeMultiplier, 0);
            var xtzRedeemFeeInMtz = xtz.RedeemFee + Math.Max((xtz.RedeemStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0);
            var xtzRefundFeeInMtz = xtz.RefundFee + Math.Max((xtz.RefundStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0);

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
                return tokenConfig.ApproveStorageLimit;

            if (type.HasFlag(TransactionType.SwapPayment))
                return (tokenConfig.ApproveStorageLimit * 2 + tokenConfig.InitiateStorageLimit) * tokenConfig.StorageFeeMultiplier;

            if (type.HasFlag(TransactionType.SwapRefund))
                return (tokenConfig.RefundStorageLimit - tokenConfig.ActivationStorage) * tokenConfig.StorageFeeMultiplier;

            if (type.HasFlag(TransactionType.SwapRedeem))
                return (tokenConfig.RedeemStorageLimit - tokenConfig.ActivationStorage) * tokenConfig.StorageFeeMultiplier;

            return (tokenConfig.TransferStorageLimit - tokenConfig.ActivationStorage) * tokenConfig.StorageFeeMultiplier;
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
                .GetWalletAddressAsync(xtz.Name, fromAddress.Address)
                .ConfigureAwait(false);

            var availableBalanceInMtz = xtzAddress?.AvailableBalance() ?? 0;

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

            var restBalanceInTokenDigits = fromAddress.AvailableBalance() - amount;

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
                .GetTokenAddressAsync(TokenType, _tokenContract, _tokenId, address)
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
                .GetUnspentTokenAddressesAsync(TokenType, _tokenContract, _tokenId)
                .ConfigureAwait(false);

            foreach (var address in addresses)
                balance += address.Balance;

            return new Balance(balance, unconfirmedIncome: 0, unconfirmedOutcome: 0);
        }

        public async Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            var scanner = new TezosTokensWalletScanner(_tezosAccount);

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
            var scanner = new TezosTokensWalletScanner(_tezosAccount);

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
            KeyIndex keyIndex,
            int keyType)
        {
            return DivideAddressAsync(
                account: keyIndex.Account,
                chain: keyIndex.Chain,
                index: keyIndex.Index,
                keyType: keyType);
        }

        public Task<WalletAddress> DivideAddressAsync(
            uint account,
            uint chain,
            uint index,
            int keyType)
        {
            var walletAddress = Wallet.GetAddress(
                currency: TokenConfig,
                account: account,
                chain: chain,
                index: index,
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
                    "FA12" => "fa1.2",
                    "FA2" => "fa2",
                    _ => throw new NotSupportedException($"Not supported TokenType: {TokenType ?? "<null>"}")
                },
                Decimals = tokenConfig?.Digits ?? 0
            };

            return Task.FromResult(walletAddress);
        }

        public Task<WalletAddress> GetAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return LocalStorage
                .GetTokenAddressAsync(TokenType, _tokenContract, _tokenId, address);
        }

        public Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return LocalStorage
                .GetTokenAddressesByContractAsync(_tokenContract);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return LocalStorage
                .GetUnspentTokenAddressesAsync(TokenType, _tokenContract, _tokenId);
        }

        public async Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            // 1. try to find address with tokens
            var unspentAddresses = await LocalStorage
                .GetUnspentTokenAddressesAsync(TokenType, _tokenContract, _tokenId)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return unspentAddresses.MaxBy(w => w.AvailableBalance());

            // 2. try to find xtz address with max balance
            var unspentTezosAddresses = await LocalStorage
                .GetUnspentAddressesAsync(TezosConfig.Xtz)
                .ConfigureAwait(false);

            if (unspentTezosAddresses.Any())
            {
                var xtzAddress = unspentTezosAddresses.MaxBy(a => a.AvailableBalance());

                var tokenAddress = await LocalStorage
                    .GetTokenAddressAsync(
                        currency: TokenType,
                        tokenContract: _tokenContract,
                        tokenId: _tokenId,
                        address: xtzAddress.Address)
                    .ConfigureAwait(false);

                if (tokenAddress != null)
                    return tokenAddress;

                return await DivideAddressAsync(
                        keyIndex: xtzAddress.KeyIndex,
                        keyType: xtzAddress.KeyType)
                    .ConfigureAwait(false);
            }

            // 3. use xtz redeem address
            var xtzRedeemAddress = await _tezosAccount
                .GetRedeemAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            var tokenRedeemAddress = await LocalStorage
                .GetTokenAddressAsync(
                    currency: TokenType,
                    tokenContract: _tokenContract,
                    tokenId: _tokenId,
                    address: xtzRedeemAddress.Address)
                .ConfigureAwait(false);

            if (tokenRedeemAddress != null)
                return tokenRedeemAddress;

            return await DivideAddressAsync(
                    keyIndex: xtzRedeemAddress.KeyIndex,
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

        private async Task<TezosTokenTransferMetadata> ResolveTransactionMetadataAsync(
            TezosTokenTransfer tx,
            CancellationToken cancellationToken = default)
        {
            var result = new TezosTokenTransferMetadata
            {
                Id = tx.Id,
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