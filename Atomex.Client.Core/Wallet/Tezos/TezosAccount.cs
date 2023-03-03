using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Netezos.Forging.Models;
using Netezos.Encoding;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Tzkt.Operations;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallets.Bips;
using Atomex.Wallets.Tezos;

namespace Atomex.Wallet.Tezos
{
    public class TezosAccount : CurrencyAccount, IEstimatable, IHasTokens
    {
        private static TezosOperationPerBlockManager _operationsBatcher;
        public static TezosOperationPerBlockManager OperationsBatcher
        {
            get
            {
                var instance = _operationsBatcher;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _operationsBatcher, new TezosOperationPerBlockManager(), null);
                    instance = _operationsBatcher;
                }

                return instance;
            }
        }

        private readonly TezosRevealChecker _tezosRevealChecker;
        private readonly TezosAllocationChecker _tezosAllocationChecker;

        public TezosAccount(
            ICurrencies currencies,
            IHdWallet wallet,
            ILocalStorage dataRepository)
                : base(TezosConfig.Xtz, currencies, wallet, dataRepository)
        {
            var xtz = Config;

            _tezosRevealChecker = new TezosRevealChecker(xtz);
            _tezosAllocationChecker = new TezosAllocationChecker(xtz);
        }

        public TezosConfig Config => Currencies.Get<TezosConfig>(Currency);

        #region Sending

        public Task<Result<TezosOperationRequestResult>> SendTransactionsAsync(
            IEnumerable<TezosOperationParameters> transactions,
            CancellationToken cancellationToken = default)
        {
            return OperationsBatcher
                .SendOperationsAsync(
                    account: this,
                    operationsParameters: transactions,
                    cancellationToken: cancellationToken);
        }

        public async Task<Result<TezosOperationRequestResult>> SendTransactionAsync(
            string from,
            string to,
            long amount,
            Fee fee,
            GasLimit gasLimit,
            StorageLimit storageLimit,
            string entrypoint = null,
            string parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (fee == null)
                throw new ArgumentNullException(nameof(fee), "Fee must be not null. Please use Fee.FromValue() or Fee.FromNetwork()");

            if (gasLimit == null)
                throw new ArgumentNullException(nameof(gasLimit), "GasLimit must be not null. Please use GasLimit.FromValue() or GasLimit.FromNetwork()");

            if (storageLimit == null)
                throw new ArgumentNullException(nameof(storageLimit), "StorageLimit must be not null. Please use StorageLimit.FromValue() or StorageLimit.FromNetwork()");

            return await OperationsBatcher
                .SendOperationsAsync(
                    account: this,
                    operationsParameters: new List<TezosOperationParameters> {
                        new TezosOperationParameters {
                            Content = new TransactionContent
                            {
                                Source       = from,
                                Amount       = amount,
                                Destination  = to,
                                Fee          = fee.Value,
                                GasLimit     = gasLimit.Value,
                                StorageLimit = storageLimit.Value,
                                Parameters   = parameters != null
                                    ? new Parameters
                                    {
                                        Entrypoint = entrypoint,
                                        Value = Micheline.FromJson(parameters)
                                    }
                                    : null
                            },
                            From         = from,
                            Fee          = fee,
                            GasLimit     = gasLimit,
                            StorageLimit = storageLimit
                        }
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<string>> SendUnmanagedOperationAsync(
            TezosOperationRequest operation,
            CancellationToken cancellationToken = default)
        {
            if (operation.IsManaged())
                throw new InvalidOperationException("Method must be used for sending unmanaged operations only");

            return await SendOperationAsync(operation, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Send managed operation without single per block operation control. If the replaced operation has already been confirmed, the method will return an error.
        /// </summary>
        /// <param name="operation">Replacement operation</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Operation if success, otherwise error</returns>
        public async Task<Result<string>> ReplaceOperationAsync(
            TezosOperationRequest operation,
            CancellationToken cancellationToken = default)
        {
            if (!operation.IsManaged())
                throw new InvalidOperationException("Method must be used for sending managed operations only");

            return await SendOperationAsync(operation, cancellationToken)
                .ConfigureAwait(false);
        }

        internal async Task<Result<string>> SendOperationAsync(
            TezosOperationRequest operationRequest,
            CancellationToken cancellationToken = default)
        {
            // sign the operation
            var (_, error) = await SignAsync(operationRequest, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            var rpc = new TezosRpc(Config.GetRpcSettings());

            var forgedOperationBytes = await operationRequest
                .ForgeAsync()
                .ConfigureAwait(false);

            var signedBytesInHex = forgedOperationBytes.ToHexString() + operationRequest.Signature.ToHexString();

            var operationId = await rpc
                .InjectOperationsAsync(signedBytesInHex, cancellationToken)
                .ConfigureAwait(false);

            var operation = new TezosOperation(operationRequest, operationId);

            // save operation in local db
            await LocalStorage
                .UpsertTransactionAsync(
                    tx: operation,
                    notifyIfNewOrChanged: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return operationId;
        }

        #endregion Sending

        #region Signing

        public async Task<Result<bool>> SignAsync(
            TezosOperationRequest operation,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var walletAddress = await LocalStorage
                    .GetAddressAsync(
                        currency: Currency,
                        address: operation.From,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var forgedOperationWithPrefix = await operation
                    .ForgeAsync(addOperationPrefix: true)
                    .ConfigureAwait(false);

                operation.Signature = await Wallet
                    .SignHashAsync(
                        hash: forgedOperationWithPrefix,
                        address: walletAddress,
                        currency: Config,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                return true;
            }
            catch (Exception e)
            {
                var error = $"Signing error: {e.Message}";

                Log.Error(error);

                return new Error(Errors.SigningError, error);
            }
        }

        #endregion Signing

        public async Task<long> EstimateFeeAsync(
            string from,
            string to,
            TransactionType type,
            CancellationToken cancellationToken = default)
        {
            var txFeeInMtz = await FeeInMtzByType(
                    type: type,
                    from: from,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInMtz = await StorageFeeInMtzByTypeAsync(
                    type: type,
                    to: to,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return txFeeInMtz + storageFeeInMtz;
        }

        public async Task<Result<decimal>> EstimateSwapPaymentFeeAsync(
            IFromSource from,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            var fromAddress = (from as FromAddress)?.Address;

            return await EstimateFeeAsync(
                    from: fromAddress,
                    to: null,
                    type: TransactionType.SwapPayment,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<MaxAmountEstimation> EstimateMaxAmountToSendAsync(
            string from,
            string to,
            TransactionType type,
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

            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return new MaxAmountEstimation {
                    Error = new Error(Errors.AddressNotFound, Resources.AddressNotFoundInLocalDb)
                };

            var reserveFeeInMtz = ReserveFeeInMtz();

            var feeInMtz = await FeeInMtzByType(
                    type: type,
                    from: fromAddress.Address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInMtz = await StorageFeeInMtzByTypeAsync(
                    type: type,
                    to: to,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var requiredFeeInMtz = feeInMtz +
                storageFeeInMtz +
                (reserve ? reserveFeeInMtz : 0);

            var requiredInMtz = requiredFeeInMtz + Config.MicroTezReserve;

            var restAmountInMtz = fromAddress.Balance - requiredInMtz;

            if (restAmountInMtz < 0)
                return new MaxAmountEstimation {
                    Amount = restAmountInMtz,
                    Fee = requiredFeeInMtz,
                    Reserved = reserveFeeInMtz,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFundsToCoverFees),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsToCoverFeesDetails,
                        requiredInMtz,
                        Currency,
                        fromAddress.Balance)
                };

            return new MaxAmountEstimation
            {
                Amount   = restAmountInMtz,
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
                to: null,
                type: TransactionType.SwapPayment,
                reserve: reserve,
                cancellationToken: cancellationToken);
        }

        private async Task<long> FeeInMtzByType(
            TransactionType type,
            string from,
            CancellationToken cancellationToken = default)
        {
            var xtz = Config;

            var isRevealed = await IsRevealedSourceAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var revealFeeInMtz = !isRevealed
                ? xtz.RevealFee
                : 0;

            if (type.HasFlag(TransactionType.SwapPayment))
                return xtz.InitiateFee + revealFeeInMtz;

            if (type.HasFlag(TransactionType.SwapRefund))
                return xtz.RefundFee + revealFeeInMtz;

            if (type.HasFlag(TransactionType.SwapRedeem))
                return xtz.RedeemFee + revealFeeInMtz;

            return xtz.Fee + revealFeeInMtz;
        }

        private long ReserveFeeInMtz()
        {
            var xtz = Config;

            var redeemFeeInMtz = xtz.RedeemFee + Math.Max((xtz.RedeemStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0);
            var refundFeeInMtz = xtz.RefundFee + Math.Max((xtz.RefundStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0);

            return Math.Max(redeemFeeInMtz, refundFeeInMtz) + xtz.RevealFee + xtz.MicroTezReserve;
        }

        private async Task<long> StorageFeeInMtzByTypeAsync(
            TransactionType type,
            string to,
            CancellationToken cancellationToken = default)
        {
            var xtz = Config;

            var isActive = await IsAllocatedDestinationAsync(to, cancellationToken)
                .ConfigureAwait(false);

            if (type.HasFlag(TransactionType.SwapPayment))
                return xtz.InitiateStorageLimit * xtz.StorageFeeMultiplier;

            if (type.HasFlag(TransactionType.SwapRefund))
                return isActive
                    ? Math.Max((xtz.RefundStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0) // without activation storage fee
                    : xtz.RefundStorageLimit * xtz.StorageFeeMultiplier;

            if (type.HasFlag(TransactionType.SwapRedeem))
                return isActive
                    ? Math.Max((xtz.RedeemStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0) // without activation storage fee
                    : xtz.RedeemStorageLimit * xtz.StorageFeeMultiplier;

            return isActive
                ? Math.Max((xtz.StorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0) // without activation storage fee
                : xtz.StorageLimit * xtz.StorageFeeMultiplier;
        }

        public async Task<bool> IsRevealedSourceAsync(
            string from,
            CancellationToken cancellationToken = default)
        {
            return !string.IsNullOrEmpty(from) && await _tezosRevealChecker
                .IsRevealedAsync(from, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<bool> IsAllocatedDestinationAsync(
            string to,
            CancellationToken cancellationToken = default)
        {
            return !string.IsNullOrEmpty(to) && await _tezosAllocationChecker
                .IsAllocatedAsync(to, cancellationToken)
                .ConfigureAwait(false);
        }

        #region Balances

        public override async Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            var scanner = new TezosWalletScanner(this);

            await scanner
                .UpdateBalanceAsync(skipUsed: false, cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var scanner = new TezosWalletScanner(this);

            await scanner
                .UpdateBalanceAsync(address, cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion Balances

        #region Addresses

        public async Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = await GetUnspentAddressesAsync(cancellationToken)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return unspentAddresses.MaxBy(w => w.Balance);

            var keyType = CurrencyConfig.StandardKey;

            foreach (var chain in new[] {Bip44.Internal, Bip44.External})
            {
                var keyPathPattern = Config
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
            var fa12Addresses = await LocalStorage
                .GetUnspentAddressesAsync(currency: TezosHelper.Fa12)
                .ConfigureAwait(false);

            var fa2Addresses = await LocalStorage
                .GetUnspentAddressesAsync(currency: TezosHelper.Fa12)
                .ConfigureAwait(false);

            return fa12Addresses.Concat(fa2Addresses);
        }

        public async Task<SelectedWalletAddress> CalculateFundsUsageAsync(
            string from,
            string to,
            BigInteger amount,
            long fee,
            FeeUsagePolicy feeUsagePolicy,
            TransactionType transactionType,
            CancellationToken cancellationToken = default)
        {
            var xtz = Config;
            
            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return null; // invalid address

            var txFeeInMtz = feeUsagePolicy == FeeUsagePolicy.EstimatedFee
                ? await FeeInMtzByType(
                        type: transactionType,
                        from: fromAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
                : fee;

            var storageFeeInMtz = await StorageFeeInMtzByTypeAsync(
                    type: transactionType,
                    to: to,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var restBalanceInMtz = fromAddress.Balance -
                amount -
                txFeeInMtz -
                storageFeeInMtz -
                xtz.MicroTezReserve;

            if (restBalanceInMtz < 0)
                return null; // insufficient funds

            return new SelectedWalletAddress
            {
                WalletAddress  = fromAddress,
                UsedAmount     = amount,
                UsedFee        = txFeeInMtz,
                UsedStorageFee = storageFeeInMtz
            };
        }

        #endregion Addresses

        #region Transactions

        public override async Task<IEnumerable<ITransaction>> GetUnconfirmedTransactionsAsync(
            CancellationToken cancellationToken = default)
        {
            return await LocalStorage
                .GetUnconfirmedTransactionsAsync<TezosOperation>(Currency, cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task ResolveTransactionsMetadataAsync(
            IEnumerable<ITransaction> txs,
            CancellationToken cancellationToken = default)
        {
            var resolvedMetadata = new List<ITransactionMetadata>();

            foreach (var tx in txs.Cast<TezosOperation>())
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

        public override async Task<ITransactionMetadata> ResolveTransactionMetadataAsync(
            ITransaction tx,
            CancellationToken cancellationToken = default)
        {
            return await ResolveTransactionMetadataAsync(
                    (TezosOperation)tx,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<TransactionMetadata> ResolveTransactionMetadataAsync(
            TezosOperation operation,
            CancellationToken cancellationToken = default)
        {
            var result = new TransactionMetadata
            {
                Id = operation.Id,
                Currency = operation.Currency,
                Internals = new List<InternalTransactionMetadata>(),
            };

            var fromAddress = await GetAddressAsync(operation.From, cancellationToken)
                .ConfigureAwait(false);

            var isFromSelf = fromAddress != null;

            foreach (var o in operation.Operations)
            {
                var operationType = TransactionType.Unknown;
                var operationAmount = BigInteger.Zero;
                var operationFee = BigInteger.Zero;

                if (isFromSelf)
                    operationType |= TransactionType.Output;

                if (o is TransactionOperation tx)
                {
                    var toAddress = await GetAddressAsync(tx.Target.Address)
                        .ConfigureAwait(false);

                    var isToSelf = toAddress != null;

                    if (isToSelf)
                    {
                        operationType |= TransactionType.Input;
                        operationAmount += tx.Amount;
                    }

                    if (isFromSelf)
                    {
                        operationAmount -= tx.Amount;
                        operationFee += tx.BakerFee + tx.AllocationFee + tx.StorageFee;
                    }

                    if (tx.Parameter != null)
                    {
                        operationType |= TransactionType.ContractCall;

                        if (tx.Parameter.Entrypoint == "initiate")
                            operationType |= TransactionType.SwapPayment;
                        else if (tx.Parameter.Entrypoint == "redeem")
                            operationType |= TransactionType.SwapRedeem;
                        else if (tx.Parameter.Entrypoint == "refund")
                            operationType |= TransactionType.SwapRefund;
                        else if (tx.Parameter.Entrypoint == "transfer")
                            operationType |= TransactionType.TokenTransfer;
                        else if (tx.Parameter.Entrypoint == "approve" || tx.Parameter.Entrypoint == "update_operators")
                            operationType |= TransactionType.TokenApprove;
                    }
                }
                else
                {
                    // delegations, reveals and others
                }

                result.Internals.Add(new InternalTransactionMetadata
                {
                    Type = operationType,
                    Amount = operationAmount,
                    Fee = operationFee
                });

                result.Type |= operationType;
                result.Amount += operationAmount;
                result.Fee += operationFee;
            }

            return result;
        }

        #endregion Transactions
    }
}