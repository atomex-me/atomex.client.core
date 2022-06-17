using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Core;
using Atomex.TezosTokens;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet.Tezos
{
    public class Fa2Account : TezosTokenAccount, IEstimatable
    {
        private Fa2Config Fa2Config => Currencies.Get<Fa2Config>(Currency);

        public Fa2Account(
            string currency,
            string tokenContract,
            decimal tokenId,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository,
            TezosAccount tezosAccount)
            : base(currency,
                  "FA2",
                  tokenContract,
                  tokenId,
                  currencies,
                  wallet,
                  dataRepository,
                  tezosAccount)
        {
        }

        #region Common

        public async Task<(string txId, Error error)> SendAsync(
            string from,
            string to,
            decimal amount,
            string tokenContract,
            int tokenId,
            int fee,
            bool useDefaultFee = true,
            CancellationToken cancellationToken = default)
        {
            var fa2Config = Fa2Config;
            var xtzConfig = XtzConfig;

            var fromAddress = await DataRepository
                .GetTezosTokenAddressAsync(TokenType, _tokenContract, _tokenId, from)
                .ConfigureAwait(false);

            var digitsMultiplier = (decimal)Math.Pow(10, fromAddress.TokenBalance.Decimals);

            var availableBalance = fromAddress.AvailableBalance() * digitsMultiplier;

            if (availableBalance < amount)
                return (
                    txId: null,
                    error: new Error(
                        code: Errors.InsufficientFunds,
                        description: $"Insufficient tokens. " +
                            $"Available: {fromAddress.AvailableBalance()}. " +
                            $"Required: {amount}."));

            var xtzAddress = await DataRepository
                .GetWalletAddressAsync(xtzConfig.Name, from)
                .ConfigureAwait(false);

            var isRevealed = await _tezosAccount
                .IsRevealedSourceAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInMtz = (fa2Config.TransferStorageLimit - fa2Config.ActivationStorage) * fa2Config.StorageFeeMultiplier;

            var feeInMtz = useDefaultFee
                ? fa2Config.TransferFee + (isRevealed ? 0 : fa2Config.RevealFee) + storageFeeInMtz
                : fee;

            var availableBalanceInTz = xtzAddress.AvailableBalance().ToMicroTez() - feeInMtz - xtzConfig.MicroTezReserve;

            if (availableBalanceInTz < 0)
                return (
                    txId: null,
                    error: new Error(
                        code: Errors.InsufficientFunds,
                        description: $"Insufficient funds to pay fee for address {from}. " +
                            $"Available: {xtzAddress.AvailableBalance()}. " +
                            $"Required: {feeInMtz + xtzConfig.MicroTezReserve}"));

            Log.Debug("Send {@amount} tokens from address {@address} with available balance {@balance}",
                amount,
                from,
                fromAddress.AvailableBalance());

            var storageLimit = Math.Max(fa2Config.TransferStorageLimit - fa2Config.ActivationStorage, 0); // without activation storage fee

            var tx = new TezosTransaction
            {
                Currency     = xtzConfig.Name,
                CreationTime = DateTime.UtcNow,
                From         = from,
                To           = tokenContract,
                Fee          = feeInMtz,
                GasLimit     = fa2Config.TransferGasLimit,
                StorageLimit = storageLimit,
                Params       = CreateTransferParams(tokenId, from, to, amount),
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
                keyIndex: fromAddress.KeyIndex,
                keyType: fromAddress.KeyType);

            // fill operation
            var (fillResult, isRunSuccess, hasReveal) = await tx
                .FillOperationsAsync(
                    securePublicKey: securePublicKey,
                    tezosConfig: xtzConfig,
                    headOffset: TezosConfig.HeadOffset,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var signResult = await Wallet
                .SignAsync(tx, xtzAddress, xtzConfig, cancellationToken)
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

        public override async Task<(decimal fee, bool isEnougth)> EstimateTransferFeeAsync(
            string from,
            CancellationToken cancellationToken = default)
        {
            var fa2Config = Fa2Config;
            var xtzConfig = XtzConfig;

            var xtzAddress = await _tezosAccount
                .GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var isRevealed = xtzAddress?.Address != null && await _tezosAccount
                .IsRevealedSourceAsync(xtzAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInMtz = (fa2Config.TransferStorageLimit - fa2Config.ActivationStorage) * fa2Config.StorageFeeMultiplier;

            var feeInMtz = fa2Config.TransferFee + (isRevealed ? 0 : fa2Config.RevealFee) + storageFeeInMtz + xtzConfig.MicroTezReserve;

            var availableBalanceInTez = xtzAddress != null
                ? xtzAddress.AvailableBalance()
                : 0m;

            return (
                fee: feeInMtz.ToTez(),
                isEnougth: availableBalanceInTez >= feeInMtz.ToTez());
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
                            0,               // available tokens
                            Fa2Config.Name)) // currency code
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
                            requiredFeeInTez,          // required fee
                            Fa2Config.FeeCurrencyName, // currency code
                            0m))                       // available
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
                            Fa2Config.FeeCurrencyName,      // currency code
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
                            Fa2Config.Name))                // currency code
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
            var fa2 = Fa2Config;

            var isRevealed = from != null && await _tezosAccount
                .IsRevealedSourceAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var revealFeeInTez = !isRevealed
                ? fa2.RevealFee.ToTez()
                : 0;

            if (type.HasFlag(BlockchainTransactionType.TokenApprove))
                return fa2.ApproveFee.ToTez();

            if (type.HasFlag(BlockchainTransactionType.SwapPayment))
                return fa2.ApproveFee.ToTez() * 2 + fa2.InitiateFee.ToTez() + revealFeeInTez;

            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return fa2.RefundFee.ToTez() + revealFeeInTez;

            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return fa2.RedeemFee.ToTez() + revealFeeInTez;

            return fa2.TransferFee.ToTez() + revealFeeInTez;
        }

        private decimal ReserveFee()
        {
            var xtz = XtzConfig;
            var fa2 = Fa2Config;

            return new[]
            {
                (fa2.RedeemFee + Math.Max((fa2.RedeemStorageLimit - fa2.ActivationStorage) * fa2.StorageFeeMultiplier, 0)).ToTez(),
                (fa2.RefundFee + Math.Max((fa2.RefundStorageLimit - fa2.ActivationStorage) * fa2.StorageFeeMultiplier, 0)).ToTez(),
                (xtz.RedeemFee + Math.Max((xtz.RedeemStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0)).ToTez(),
                (xtz.RefundFee + Math.Max((xtz.RefundStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0)).ToTez()

            }.Max() + fa2.RevealFee.ToTez() + XtzConfig.MicroTezReserve.ToTez();
        }

        private decimal StorageFeeByType(BlockchainTransactionType type)
        {
            var fa2 = Fa2Config;

            if (type.HasFlag(BlockchainTransactionType.TokenApprove))
                return fa2.ApproveStorageLimit.ToTez();

            if (type.HasFlag(BlockchainTransactionType.SwapPayment))
                return ((fa2.ApproveStorageLimit * 2 + fa2.InitiateStorageLimit) * fa2.StorageFeeMultiplier).ToTez();

            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return ((fa2.RefundStorageLimit - fa2.ActivationStorage) * fa2.StorageFeeMultiplier).ToTez();

            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return ((fa2.RedeemStorageLimit - fa2.ActivationStorage) * fa2.StorageFeeMultiplier).ToTez();

            return ((fa2.TransferStorageLimit - fa2.ActivationStorage) * fa2.StorageFeeMultiplier).ToTez();
        }

        #endregion Common

        #region Helpers

        private JObject CreateTransferParams(
            int tokenId,
            string from,
            string to,
            decimal amount)
        {
            return JObject.FromObject(new
            {
                entrypoint = "transfer",
                value = new object[]
                {
                    new
                    {
                        prim = "Pair",
                        args = new object[]
                        {
                            new
                            {
                                @string = from
                            },
                            new object[]
                            {
                                new
                                {
                                    prim = "Pair",
                                    args = new object[]
                                    {
                                        new
                                        {
                                            @string = to,
                                        },
                                        new
                                        {
                                            prim = "Pair",
                                            args = new object[]
                                            {
                                                new
                                                {
                                                    @int = tokenId.ToString()
                                                },
                                                new
                                                {
                                                    @int = string.Format("{0:0}", amount)
        }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            });
        }

        #endregion Helpers
    }
}