using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Abstract;
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
            decimal amount,
            decimal fee,
            bool useDefaultFee = true,
            CancellationToken cancellationToken = default)
        {
            var tokenConfig = TokenConfig;
            var xtzConfig = XtzConfig;

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

            var digitsMultiplier = addressFeeUsage.WalletAddress.TokenBalance.Decimals == 0
                ? tokenConfig.DigitsMultiplier
                : (decimal)Math.Pow(10, addressFeeUsage.WalletAddress.TokenBalance.Decimals);

            var addressAmountInDigits = addressFeeUsage.UsedAmount.ToTokenDigits(digitsMultiplier);

            Log.Debug("Send {@amount} tokens from address {@address} with available balance {@balance}",
                addressAmountInDigits,
                addressFeeUsage.WalletAddress.Address,
                addressFeeUsage.WalletAddress.AvailableBalance());

            var storageLimit = Math.Max(tokenConfig.TransferStorageLimit - tokenConfig.ActivationStorage, 0); // without activation storage fee

            var tx = new TezosTransaction
            {
                Currency     = xtzConfig.Name,
                CreationTime = DateTime.UtcNow,
                From         = from,
                To           = _tokenContract,
                Fee          = addressFeeUsage.UsedFee.ToMicroTez(),
                GasLimit     = tokenConfig.TransferGasLimit,
                StorageLimit = storageLimit,
                Params       = CreateTransferParams(from, to, addressAmountInDigits),
                Type         = TransactionType.Output | TransactionType.TokenCall,

                UseRun              = useDefaultFee,
                UseSafeStorageLimit = true,
                UseOfflineCounter   = true
            };

            using var addressLock = await _tezosAccount.AddressLocker
                .GetLockAsync(from, cancellationToken)
                .ConfigureAwait(false);

            // temporary fix: check operation sequence
            await TezosOperationsSequencer
                .WaitAsync(from, _tezosAccount, cancellationToken)
                .ConfigureAwait(false);

            using var securePublicKey = Wallet.GetPublicKey(
                currency: xtzConfig,
                keyIndex: addressFeeUsage.WalletAddress.KeyIndex,
                keyType: addressFeeUsage.WalletAddress.KeyType);

            // fill operation
            var (fillResult, isRunSuccess, hasReveal) = await tx
                .FillOperationsAsync(
                    securePublicKey: securePublicKey,
                    tezosConfig: xtzConfig,
                    headOffset: TezosConfig.HeadOffset,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var signResult = await _tezosAccount
                .SignAsync(tx, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
                return new Error(
                    code: Errors.TransactionSigningError,
                    message: "Transaction signing error");

            var (txId, error) = await xtzConfig.BlockchainApi
                .TryBroadcastAsync(tx, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error.Value;

            if (txId == null)
                return new Error(
                    code: Errors.TransactionBroadcastError,
                    message: "Transaction Id is null");

            Log.Debug("Transaction successfully sent with txId: {@id}", txId);

             var _ = await _tezosAccount
                .LocalStorage
                .UpsertTransactionAsync(
                    tx: tx,
                    notifyIfNewOrChanged: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return txId;
        }

        public async Task<decimal> EstimateFeeAsync(
            string from,
            TransactionType type,
            CancellationToken cancellationToken = default)
        {
            var txFeeInTez = await FeeByType(
                    type: type,
                    from: from,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInTez = StorageFeeByType(type);

            return txFeeInTez + storageFeeInTez;
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

        public virtual async Task<(decimal fee, bool isEnougth)> EstimateTransferFeeAsync(
            string from,
            CancellationToken cancellationToken = default)
        {
            var xtzAddress = await _tezosAccount
                .GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var txFeeInTez = await FeeByType(
                    type: TransactionType.Output,
                    from: xtzAddress?.Address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInTez = StorageFeeByType(
                type: TransactionType.Output);

            var requiredFeeInTez = txFeeInTez + storageFeeInTez + XtzConfig.MicroTezReserve.ToTez();

            var availableBalanceInTez = xtzAddress != null
                ? xtzAddress.AvailableBalance()
                : 0m;

            return (
                fee: requiredFeeInTez,
                isEnougth: availableBalanceInTez >= requiredFeeInTez);
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

            var reserveFee = ReserveFee();

            var xtz = XtzConfig;

            var feeInTez = await FeeByType(
                    type: type,
                    from: fromAddress.Address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInTez = StorageFeeByType(type);

            var requiredFeeInTez = feeInTez +
                storageFeeInTez +
                (reserve ? reserveFee : 0) +
                xtz.MicroTezReserve.ToTez();

            var xtzAddress = await LocalStorage
                .GetWalletAddressAsync(xtz.Name, fromAddress.Address)
                .ConfigureAwait(false);

            if (xtzAddress == null)
                return new MaxAmountEstimation
                {
                    Fee = requiredFeeInTez,
                    Reserved = reserveFee,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFundsToCoverFees),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsToCoverFeesDetails,
                        requiredFeeInTez,            // required fee
                        TokenConfig.FeeCurrencyName, // currency code
                        0m)                          // available
                };

            var restBalanceInTez = xtzAddress.AvailableBalance() - requiredFeeInTez;

            if (restBalanceInTez < 0)
                return new MaxAmountEstimation
                {
                    Fee = requiredFeeInTez,
                    Reserved = reserveFee,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFundsToCoverFees),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsToCoverFeesDetails,
                        requiredFeeInTez,              // required fee
                        TokenConfig.FeeCurrencyName,   // currency code
                        xtzAddress.AvailableBalance()) // available
                };

            if (fromAddress.AvailableBalance() <= 0)
                return new MaxAmountEstimation
                {
                    Fee = requiredFeeInTez,
                    Reserved = reserveFee,
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
                Amount = fromAddress.AvailableBalance(),
                Fee = requiredFeeInTez,
                Reserved = reserveFee
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

        private async Task<decimal> FeeByType(
            TransactionType type,
            string from,
            CancellationToken cancellationToken = default)
        {
            var tokenConfig = TokenConfig;

            var isRevealed = from != null && await _tezosAccount
                .IsRevealedSourceAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var revealFeeInTez = !isRevealed
                ? tokenConfig.RevealFee.ToTez()
                : 0;

            if (type.HasFlag(TransactionType.TokenApprove))
                return tokenConfig.ApproveFee.ToTez();

            if (type.HasFlag(TransactionType.SwapPayment))
                return tokenConfig.ApproveFee.ToTez() * 2 + tokenConfig.InitiateFee.ToTez() + revealFeeInTez;

            if (type.HasFlag(TransactionType.SwapRefund))
                return tokenConfig.RefundFee.ToTez() + revealFeeInTez;

            if (type.HasFlag(TransactionType.SwapRedeem))
                return tokenConfig.RedeemFee.ToTez() + revealFeeInTez;

            return tokenConfig.TransferFee.ToTez() + revealFeeInTez;
        }

        private decimal ReserveFee()
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
            
            return (maxTxFeeInMtz + tokenConfig.RevealFee + XtzConfig.MicroTezReserve).ToTez();
        }

        private decimal StorageFeeByType(TransactionType type)
        {
            var tokenConfig = TokenConfig;

            if (type.HasFlag(TransactionType.TokenApprove))
                return tokenConfig.ApproveStorageLimit.ToTez();

            if (type.HasFlag(TransactionType.SwapPayment))
                return ((tokenConfig.ApproveStorageLimit * 2 + tokenConfig.InitiateStorageLimit) * tokenConfig.StorageFeeMultiplier).ToTez();

            if (type.HasFlag(TransactionType.SwapRefund))
                return ((tokenConfig.RefundStorageLimit - tokenConfig.ActivationStorage) * tokenConfig.StorageFeeMultiplier).ToTez();

            if (type.HasFlag(TransactionType.SwapRedeem))
                return ((tokenConfig.RedeemStorageLimit - tokenConfig.ActivationStorage) * tokenConfig.StorageFeeMultiplier).ToTez();

            return ((tokenConfig.TransferStorageLimit - tokenConfig.ActivationStorage) * tokenConfig.StorageFeeMultiplier).ToTez();
        }

        protected async Task<SelectedWalletAddress> CalculateFundsUsageAsync(
            string from,
            decimal amount,
            decimal fee,
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

            var availableBalanceInTez = xtzAddress?.AvailableBalance() ?? 0m;

            var txFeeInTez = feeUsagePolicy == FeeUsagePolicy.EstimatedFee
                ? await FeeByType(
                        type: transactionType,
                        from: fromAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
                : fee;

            var storageFeeInTez = StorageFeeByType(transactionType);

            var restBalanceInTez = availableBalanceInTez -
                txFeeInTez -
                storageFeeInTez -
                xtz.MicroTezReserve.ToTez();

            if (restBalanceInTez < 0)
            {
                Log.Debug("Unsufficient XTZ ammount for Tezos token processing on address {@address} with available balance {@balance} and needed amount {@amount}",
                    fromAddress.Address,
                    availableBalanceInTez,
                    txFeeInTez + storageFeeInTez + xtz.MicroTezReserve.ToTez());

                return null;
            }

            var restBalanceInTokens = fromAddress.AvailableBalance() - amount;

            if (restBalanceInTokens < 0) // todo: log?
                return null;

            return new SelectedWalletAddress
            {
                WalletAddress = fromAddress,
                UsedAmount = amount,
                UsedFee = txFeeInTez,
                UsedStorageFee = storageFeeInTez
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
            var balance = 0m;

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

        protected abstract JObject CreateTransferParams(
            string from,
            string to,
            decimal amount);

        #endregion Helpers

        #region Transactions

        public Task<IEnumerable<ITransaction>> GetUnconfirmedTransactionsAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<ITransaction>());

        #endregion Transactions
    }
}