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
        public event EventHandler<CurrencyEventArgs> BalanceUpdated;

        protected readonly string _tokenContract;
        protected readonly int _tokenId;
        protected readonly TezosAccount _tezosAccount;

        public string Currency { get; }
        public string TokenType { get; }
        public ICurrencies Currencies { get; }
        public IHdWallet Wallet { get; }
        public IAccountDataRepository DataRepository { get; }

        protected TezosConfig XtzConfig => Currencies.Get<TezosConfig>(TezosConfig.Xtz);
        protected TezosTokenConfig TokenConfig => Currencies.Get<TezosTokenConfig>(Currency);

        public TezosTokenAccount(
            string tokenType,
            string tokenContract,
            int tokenId,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository,
            TezosAccount tezosAccount)
        {
            TokenType      = tokenType ?? throw new ArgumentNullException(nameof(tokenType));
            Currencies     = currencies ?? throw new ArgumentNullException(nameof(currencies));
            Wallet         = wallet ?? throw new ArgumentNullException(nameof(wallet));
            DataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));

            _tokenContract = tokenContract ?? throw new ArgumentNullException(nameof(tokenContract));
            _tokenId       = tokenId;
            _tezosAccount  = tezosAccount ?? throw new ArgumentNullException(nameof(tezosAccount));

            Currency = Currencies
                .Where(c => c is TezosTokenConfig token && token.TokenContractAddress == tokenContract && token.TokenId == tokenId)
                .FirstOrDefault()?.Name ?? tokenType;
        }

        public async Task<(string txId, Error error)> SendAsync(
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
                    transactionType: BlockchainTransactionType.Output,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (addressFeeUsage == null)
                return (
                    txId: null,
                    error: new Error(
                        code: Errors.InsufficientFunds,
                        description: "Insufficient funds"));

            var digitsMultiplier = tokenConfig.DigitsMultiplier != 0
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
                Type         = BlockchainTransactionType.Output | BlockchainTransactionType.TokenCall,

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

            var signResult = await Wallet
                .SignAsync(tx, addressFeeUsage.WalletAddress, xtzConfig, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
                return (
                    txId: null,
                    error: new Error(
                        code: Errors.TransactionSigningError,
                        description: "Transaction signing error"));

            var broadcastResult = await xtzConfig.BlockchainApi
                .TryBroadcastAsync(tx, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
                return (txId: null, error: broadcastResult.Error);

            var txId = broadcastResult.Value;

            if (txId == null)
                return (
                    txId: null,
                    error: new Error(
                        code: Errors.TransactionBroadcastError,
                        description: "Transaction Id is null"));

            Log.Debug("Transaction successfully sent with txId: {@id}", txId);

            await _tezosAccount
                .UpsertTransactionAsync(
                    tx: tx,
                    updateBalance: false,
                    notifyIfUnconfirmed: true,
                    notifyIfBalanceUpdated: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return (txId, error: null);
        }

        public async Task<decimal> EstimateFeeAsync(
            string from,
            BlockchainTransactionType type,
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
                    type: BlockchainTransactionType.SwapPayment,
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
                    type: BlockchainTransactionType.Output,
                    from: xtzAddress?.Address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInTez = StorageFeeByType(
                type: BlockchainTransactionType.Output);

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
            BlockchainTransactionType type,
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
                        description: Resources.InsufficientFunds,
                        details: string.Format(
                            Resources.InsufficientFundsDetails,
                            0,                 // available tokens
                            TokenConfig.Name)) // currency code
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

            var xtzAddress = await DataRepository
                .GetWalletAddressAsync(xtz.Name, fromAddress.Address)
                .ConfigureAwait(false);

            if (xtzAddress == null)
                return new MaxAmountEstimation
                {
                    Fee = requiredFeeInTez,
                    Reserved = reserveFee,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        description: Resources.InsufficientFundsToCoverFees,
                        details: string.Format(
                            Resources.InsufficientFundsToCoverFeesDetails,
                            requiredFeeInTez,            // required fee
                            TokenConfig.FeeCurrencyName, // currency code
                            0m))                         // available
                };

            var restBalanceInTez = xtzAddress.AvailableBalance() - requiredFeeInTez;

            if (restBalanceInTez < 0)
                return new MaxAmountEstimation
                {
                    Fee = requiredFeeInTez,
                    Reserved = reserveFee,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        description: Resources.InsufficientFundsToCoverFees,
                        details: string.Format(
                            Resources.InsufficientFundsToCoverFeesDetails,
                            requiredFeeInTez,               // required fee
                            TokenConfig.FeeCurrencyName,    // currency code
                            xtzAddress.AvailableBalance())) // available
                };

            if (fromAddress.AvailableBalance() <= 0)
                return new MaxAmountEstimation
                {
                    Fee = requiredFeeInTez,
                    Reserved = reserveFee,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        description: Resources.InsufficientFunds,
                        details: string.Format(
                            Resources.InsufficientFundsDetails,
                            fromAddress.AvailableBalance(), // available tokens
                            TokenConfig.Name))              // currency code
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
                type: BlockchainTransactionType.SwapPayment,
                reserve: reserve,
                cancellationToken: cancellationToken);
        }

        private async Task<decimal> FeeByType(
            BlockchainTransactionType type,
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

            if (type.HasFlag(BlockchainTransactionType.TokenApprove))
                return tokenConfig.ApproveFee.ToTez();

            if (type.HasFlag(BlockchainTransactionType.SwapPayment))
                return tokenConfig.ApproveFee.ToTez() * 2 + tokenConfig.InitiateFee.ToTez() + revealFeeInTez;

            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return tokenConfig.RefundFee.ToTez() + revealFeeInTez;

            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return tokenConfig.RedeemFee.ToTez() + revealFeeInTez;

            return tokenConfig.TransferFee.ToTez() + revealFeeInTez;
        }

        private decimal ReserveFee()
        {
            var xtz = XtzConfig;
            var tokenConfig = TokenConfig;

            return new[]
            {
                (tokenConfig.RedeemFee + Math.Max((tokenConfig.RedeemStorageLimit - tokenConfig.ActivationStorage) * tokenConfig.StorageFeeMultiplier, 0)).ToTez(),
                (tokenConfig.RefundFee + Math.Max((tokenConfig.RefundStorageLimit - tokenConfig.ActivationStorage) * tokenConfig.StorageFeeMultiplier, 0)).ToTez(),
                (xtz.RedeemFee + Math.Max((xtz.RedeemStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0)).ToTez(),
                (xtz.RefundFee + Math.Max((xtz.RefundStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0)).ToTez()

            }.Max() + tokenConfig.RevealFee.ToTez() + XtzConfig.MicroTezReserve.ToTez();
        }

        private decimal StorageFeeByType(BlockchainTransactionType type)
        {
            var tokenConfig = TokenConfig;

            if (type.HasFlag(BlockchainTransactionType.TokenApprove))
                return tokenConfig.ApproveStorageLimit.ToTez();

            if (type.HasFlag(BlockchainTransactionType.SwapPayment))
                return ((tokenConfig.ApproveStorageLimit * 2 + tokenConfig.InitiateStorageLimit) * tokenConfig.StorageFeeMultiplier).ToTez();

            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return ((tokenConfig.RefundStorageLimit - tokenConfig.ActivationStorage) * tokenConfig.StorageFeeMultiplier).ToTez();

            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return ((tokenConfig.RedeemStorageLimit - tokenConfig.ActivationStorage) * tokenConfig.StorageFeeMultiplier).ToTez();

            return ((tokenConfig.TransferStorageLimit - tokenConfig.ActivationStorage) * tokenConfig.StorageFeeMultiplier).ToTez();
        }

        protected async Task<SelectedWalletAddress> CalculateFundsUsageAsync(
            string from,
            decimal amount,
            decimal fee,
            FeeUsagePolicy feeUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default)
        {
            var xtz = XtzConfig;

            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return null; // invalid address

            var xtzAddress = await DataRepository
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
            var walletAddress = await DataRepository
                .GetTezosTokenAddressAsync(TokenType, _tokenContract, _tokenId, address)
                .ConfigureAwait(false);

            return walletAddress != null
                ? new Balance(
                    walletAddress.Balance,
                    walletAddress.UnconfirmedIncome,
                    walletAddress.UnconfirmedOutcome)
                : new Balance();
        }

        public virtual Balance GetBalance()
        {
            var balance = 0m;

            var addresses = DataRepository
                .GetUnspentTezosTokenAddressesAsync(TokenType, _tokenContract, _tokenId)
                .WaitForResult();

            foreach (var address in addresses)
                balance += address.Balance;

            return new Balance(balance, unconfirmedIncome: 0, unconfirmedOutcome: 0);
        }

        public Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var scanner = new TezosTokensScanner(_tezosAccount);

                await scanner
                    .UpdateBalanceAsync(
                        tokenContract: _tokenContract,
                        tokenId: _tokenId,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            }, cancellationToken);
        }

        public Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var scanner = new TezosTokensScanner(_tezosAccount);

                await scanner
                    .UpdateBalanceAsync(
                        address: address,
                        tokenContract: _tokenContract,
                        tokenId: _tokenId,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            }, cancellationToken);
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

        public async Task<WalletAddress> DivideAddressAsync(
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

            walletAddress.TokenBalance = new TokenBalance
            {
                Contract = _tokenContract,
                TokenId = _tokenId,
                Symbol = Currencies
                    .Where(c => c is TezosTokenConfig token && token.TokenContractAddress == _tokenContract && token.TokenId == _tokenId)
                    .FirstOrDefault()?.DisplayedName
            };

            await DataRepository
                .TryInsertTezosTokenAddressAsync(walletAddress)
                .ConfigureAwait(false);

            return walletAddress;
        }

        public async Task<WalletAddress> GetAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await DataRepository
                .GetTezosTokenAddressAsync(TokenType, _tokenContract, _tokenId, address)
                .ConfigureAwait(false);

            return walletAddress?.ResolvePublicKey(Currencies, Wallet);
        }

        public Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return DataRepository
                .GetTezosTokenAddressesByContractAsync(_tokenContract);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return DataRepository
                .GetUnspentTezosTokenAddressesAsync(TokenType, _tokenContract, _tokenId);
        }

        public async Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            // 1. try to find address with tokens
            var unspentAddresses = await DataRepository
                .GetUnspentTezosTokenAddressesAsync(TokenType, _tokenContract, _tokenId)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return unspentAddresses
                    .MaxBy(w => w.AvailableBalance())
                    .ResolvePublicKey(Currencies, Wallet);

            // 2. try to find xtz address with max balance
            var unspentTezosAddresses = await DataRepository
                .GetUnspentAddressesAsync(TezosConfig.Xtz)
                .ConfigureAwait(false);

            if (unspentTezosAddresses.Any())
            {
                var xtzAddress = unspentTezosAddresses.MaxBy(a => a.AvailableBalance());

                var tokenAddress = await DataRepository
                    .GetTezosTokenAddressAsync(
                        currency: TokenType,
                        tokenContract: _tokenContract,
                        tokenId: _tokenId,
                        address: xtzAddress.Address)
                    .ConfigureAwait(false);

                if (tokenAddress != null)
                    return tokenAddress.ResolvePublicKey(Currencies, Wallet);

                return await DivideAddressAsync(
                        keyIndex: xtzAddress.KeyIndex,
                        keyType: xtzAddress.KeyType)
                    .ConfigureAwait(false);
            }

            // 3. use xtz redeem address
            var xtzRedeemAddress = await _tezosAccount
                .GetRedeemAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            var tokenRedeemAddress = await DataRepository
                .GetTezosTokenAddressAsync(
                    currency: TokenType,
                    tokenContract: _tokenContract,
                    tokenId: _tokenId,
                    address: xtzRedeemAddress.Address)
                .ConfigureAwait(false);

            if (tokenRedeemAddress != null)
                return tokenRedeemAddress.ResolvePublicKey(Currencies, Wallet);

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
    }
}