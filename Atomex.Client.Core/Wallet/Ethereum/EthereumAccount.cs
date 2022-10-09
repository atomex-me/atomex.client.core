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
using Atomex.Common;
using Atomex.Core;
using Atomex.EthereumTokens;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;

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

        public async Task<Error> SendAsync(
            string from,
            string to,
            decimal amount,
            decimal gasLimit,
            decimal gasPrice,
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
                gasLimit = GasLimitByType(BlockchainTransactionType.Output);

                gasPrice = Math.Floor(await ethConfig
                    .GetGasPriceAsync(cancellationToken)
                    .ConfigureAwait(false));
            }

            var addressFeeUsage = await CalculateFundsUsageAsync(
                    from: from,
                    amount: amount,
                    fee: gasLimit,
                    feePrice: gasPrice,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (addressFeeUsage == null)
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: "Insufficient funds");

            if (gasLimit < ethConfig.GasLimit)
                return new Error(
                    code: Errors.InsufficientGas,
                    description: "Insufficient gas");

            Log.Debug("Try to send {@amount} ETH with fee {@fee} from address {@address} with available balance {@balance}",
                addressFeeUsage.UsedAmount,
                addressFeeUsage.UsedFee,
                addressFeeUsage.WalletAddress.Address,
                addressFeeUsage.WalletAddress.AvailableBalance());

            // lock address to prevent nonce races
            using var addressLock = await AddressLocker
                .GetLockAsync(addressFeeUsage.WalletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            var nonceAsyncResult = await EthereumNonceManager.Instance
                .GetNonceAsync(ethConfig, addressFeeUsage.WalletAddress.Address, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (nonceAsyncResult.HasError)
                return nonceAsyncResult.Error;

            var tx = new EthereumTransaction
            {
                Currency     = ethConfig.Name,
                Type         = BlockchainTransactionType.Output,
                CreationTime = DateTime.UtcNow,
                From         = addressFeeUsage.WalletAddress.Address,
                To           = to.ToLowerInvariant(),
                Amount       = EthereumConfig.EthToWei(addressFeeUsage.UsedAmount),
                Nonce        = nonceAsyncResult.Value,
                GasPrice     = new BigInteger(EthereumConfig.GweiToWei(gasPrice)),
                GasLimit     = new BigInteger(gasLimit),
            };

            var signResult = await SignAsync(tx, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
                return new Error(
                    code: Errors.TransactionSigningError,
                    description: "Transaction signing error");

            if (!tx.Verify())
                return new Error(
                    code: Errors.TransactionVerificationError,
                    description: "Transaction verification error");

            var broadcastResult = await ethConfig.BlockchainApi
                .TryBroadcastAsync(tx, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
                return broadcastResult.Error;

            var txId = broadcastResult.Value;

            if (txId == null)
                return new Error(
                    code: Errors.TransactionBroadcastError,
                    description: "Transaction Id is null");

            Log.Debug("Transaction successfully sent with txId: {@id}", txId);

            await LocalStorage
                .UpsertTransactionAsync(
                    tx: tx,
                    notifyIfNewOrChanged: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return null;
        }

        public async Task<bool> SignAsync(
            EthereumTransaction tx,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var walletAddress = await GetAddressAsync(
                        address: tx.From,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var signature = await Wallet
                    .SignHashAsync(tx.GetRawHash(EthConfig.ChainId), walletAddress, EthConfig, cancellationToken)
                    .ConfigureAwait(false);

                tx.RlpEncodedTx = tx.GetRlpEncoded(EthConfig.ChainId, signature);

                return true;
            }
            catch (Exception e)
            {
                Log.Error(e, "[EthereumAccount] Sign error");
                return false;
            }
        }

        public async Task<decimal> EstimateFeeAsync(
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default)
        {
            var gasPrice = Math.Floor(await EthConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false));

            return EthConfig.GetFeeAmount(GasLimitByType(type), gasPrice);
        }

        public async Task<decimal?> EstimateSwapPaymentFeeAsync(
            IFromSource from,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            return await EstimateFeeAsync(
                    type: BlockchainTransactionType.SwapPayment,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<MaxAmountEstimation> EstimateMaxAmountToSendAsync(
            string from,
            BlockchainTransactionType type,
            decimal? gasLimit,
            decimal? gasPrice,
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

            var estimatedGasPrice = Math.Floor(await eth
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false));

            var feeInEth = eth.GetFeeAmount(
                gasLimit == null
                    ? GasLimitByType(type)
                    : gasLimit.Value,
                gasPrice == null
                    ? estimatedGasPrice
                    : gasPrice.Value);

            if (feeInEth == 0)
                return new MaxAmountEstimation {
                    Error = new Error(Errors.InsufficientFee, Resources.TooLowFees)
                };

            var reserveFeeInEth = ReserveFee(estimatedGasPrice);

            var requiredFeeInEth = feeInEth + (reserve ? reserveFeeInEth : 0);

            var restAmountInEth = fromAddress.AvailableBalance() - requiredFeeInEth;

            if (restAmountInEth < 0)
                return new MaxAmountEstimation {
                    Amount   = restAmountInEth,
                    Fee      = requiredFeeInEth,
                    Reserved = reserveFeeInEth,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        description: Resources.InsufficientFundsToCoverFees,
                        details: string.Format(
                            Resources.InsufficientFundsToCoverFeesDetails,
                            requiredFeeInEth,
                            Currency,
                            fromAddress.AvailableBalance()))
                };

            return new MaxAmountEstimation
            {
                Amount   = restAmountInEth,
                Fee      = feeInEth,
                Reserved = reserveFeeInEth
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
                gasLimit: null,
                gasPrice: null,
                reserve: reserve,
                cancellationToken: cancellationToken);
        }

        private decimal GasLimitByType(BlockchainTransactionType type)
        {
            var eth = EthConfig;

            if (type.HasFlag(BlockchainTransactionType.SwapPayment))
                return eth.InitiateWithRewardGasLimit;

            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return eth.RefundGasLimit;

            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return eth.RedeemGasLimit;

            return eth.GasLimit;
        }

        private decimal ReserveFee(decimal gasPrice)
        {
            var ethConfig = EthConfig;
            var erc20Config = Erc20Config;

            return Math.Max(
                ethConfig.GetFeeAmount(Math.Max(erc20Config.RefundGasLimit, erc20Config.RedeemGasLimit), gasPrice),
                ethConfig.GetFeeAmount(Math.Max(ethConfig.RefundGasLimit, ethConfig.RedeemGasLimit), gasPrice));
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

            foreach (var chain in new[] { Bip44.Internal, Bip44.External })
            {
                var lastActiveAddress = await LocalStorage
                    .GetLastActiveWalletAddressAsync(
                        currency: Currency,
                        chain: chain,
                        keyType: CurrencyConfig.StandardKey)
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
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default)
        {
            var eth = EthConfig;

            var fromAddress = await GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (fromAddress == null)
                return null; // invalid address

            var feeInEth = eth.GetFeeAmount(fee, feePrice);

            var restBalanceInEth = fromAddress.AvailableBalance() -
               amount -
               feeInEth;

            if (restBalanceInEth < 0)
                return null; // insufficient funds

            return new SelectedWalletAddress
            {
                WalletAddress = fromAddress,
                UsedAmount    = amount,
                UsedFee       = feeInEth
            };
        }

        #endregion Addresses

        #region Transactions

        public override async Task<IEnumerable<IBlockchainTransaction>> GetUnconfirmedTransactionsAsync(
            CancellationToken cancellationToken = default)
        {
            return await LocalStorage
                .GetUnconfirmedTransactionsAsync<EthereumTransaction>(Currency)
                .ConfigureAwait(false);
        }

        #endregion Transactions
    }
}