using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;
using NBitcoin;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;

namespace Atomex.Wallet.BitcoinBased
{
    public class BitcoinBasedAccount : CurrencyAccount, IAddressResolver, IEstimatable
    {
        public BitcoinBasedConfig Config => Currencies.Get<BitcoinBasedConfig>(Currency);

        public BitcoinBasedAccount(
            string currency,
            ICurrencies currencies,
            IHdWallet wallet,
            ILocalStorage dataRepository)
                : base(currency, currencies, wallet, dataRepository)
        {
        }

        #region Common

        public async Task<Error> SendAsync(
            IEnumerable<BitcoinBasedTxOutput> from,
            string to,
            decimal amount,
            decimal fee,
            DustUsagePolicy dustUsagePolicy,
            CancellationToken cancellationToken = default)
        {
            var config = Config;

            var amountInSatoshi = config.CoinToSatoshi(amount);
            var feeInSatoshi = config.CoinToSatoshi(fee);
            var requiredInSatoshi = amountInSatoshi + feeInSatoshi;

            // minimum amount and fee control
            if (amountInSatoshi < config.GetDust())
                return new Error(
                    code: Errors.InsufficientAmount,
                    description: $"Insufficient amount to send. Min non-dust amount {config.SatoshiToCoin(config.GetDust())}, actual {config.SatoshiToCoin(amountInSatoshi)}");

            from = from
                .SelectOutputsForAmount(requiredInSatoshi)
                .ToList();

            var availableInSatoshi = from.Sum(o => o.Value);

            if (!from.Any())
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: $"Insufficient funds. Required {config.SatoshiToCoin(requiredInSatoshi)}, available {config.SatoshiToCoin(availableInSatoshi)}");

            var changeAddress = await GetFreeInternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            // minimum change control
            var changeInSatoshi = availableInSatoshi - requiredInSatoshi;
            if (changeInSatoshi > 0 && changeInSatoshi < config.GetDust())
            {
                switch (dustUsagePolicy)
                {
                    case DustUsagePolicy.Warning:
                        return new Error(
                            code: Errors.InsufficientAmount,
                            description: $"Change {config.SatoshiToCoin(changeInSatoshi)} can be definded by the network as dust and the transaction will be rejected");
                    case DustUsagePolicy.AddToDestination:
                        amountInSatoshi += changeInSatoshi;
                        break;
                    case DustUsagePolicy.AddToFee:
                        feeInSatoshi += changeInSatoshi;
                        break;
                    default:
                        return new Error(
                            code: Errors.InternalError,
                            description: $"Unknown dust usage policy value {dustUsagePolicy}");
                }
            }

            var tx = config.CreatePaymentTx(
                unspentOutputs: from,
                destinationAddress: to,
                changeAddress: changeAddress.Address,
                amount: amountInSatoshi,
                fee: feeInSatoshi,
                lockTime: DateTimeOffset.MinValue);

            var signResult = await Wallet
                .SignAsync(
                    tx: tx,
                    spentOutputs: from,
                    addressResolver: this,
                    currencyConfig: config,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
                return new Error(
                    code: Errors.TransactionSigningError,
                    description: "Transaction signing error");

            if (!tx.Verify(from, out var errors, config))
                return new Error(
                    code: Errors.TransactionVerificationError,
                    description: $"Transaction verification error: {string.Join(", ", errors.Select(e => e.Description))}");

            var broadcastResult = await config.BlockchainApi
                .TryBroadcastAsync(tx, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
                return broadcastResult.Error;

            var txId = broadcastResult.Value;

            if (txId == null)
                return new Error(
                    code: Errors.TransactionBroadcastError,
                    description: "Transaction id is null");

            Log.Debug("Transaction successfully sent with txId: {@id}", txId);

            await LocalStorage
                .UpsertTransactionAsync(
                    tx: tx,
                    notifyIfNewOrChanged: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return null;
        }

        public async Task<decimal?> EstimateFeeAsync(
            IEnumerable<BitcoinInputToSign> from,
            string changeTo,
            decimal amount,
            decimal feeRate,
            CancellationToken cancellationToken = default)
        {
            var txParams = await BitcoinTransactionParams.SelectTransactionParamsByFeeRateAsync(
                    availableInputs: from,
                    destinations: new (decimal AmountInSatoshi, int Size)[]
                    {
                        (AmountInSatoshi: Config.CoinToSatoshi(amount), Size: BitcoinBasedConfig.LegacyTxOutputSize)
                    },
                    changeAddress: changeTo,
                    feeRate: feeRate,
                    currencyConfig: Config,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return txParams != null
                ? Config.SatoshiToCoin((long)txParams.FeeInSatoshi)
                : 0;
        }

        public async Task<decimal?> EstimateSwapPaymentFeeAsync(
            IFromSource from,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            var feeRate = await Config
                .GetFeeRateAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var outputs = (from as FromOutputs)?.Outputs;

            if (outputs == null || !outputs.Any())
                return Config.SatoshiToCoin((long)(feeRate * BitcoinBasedConfig.OneInputTwoOutputTxSize));

            var changeAddress = await GetFreeInternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            return await EstimateFeeAsync(
                    from: outputs.Select(o => new BitcoinInputToSign { Output = o }),
                    changeTo: changeAddress.Address,
                    amount: amount,
                    feeRate: feeRate,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public Task<MaxAmountEstimation> EstimateMaxSwapPaymentAmountAsync(
            IFromSource from,
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            var outputs = (from as FromOutputs)?.Outputs;

            return EstimateMaxAmountToSendAsync(
                outputs: outputs,
                to: null,
                fee: null,
                feeRate: null,
                cancellationToken: cancellationToken);
        }

        public async Task<MaxAmountEstimation> EstimateMaxAmountToSendAsync(
            IEnumerable<BitcoinBasedTxOutput> outputs,
            string to,
            decimal? fee,
            decimal? feeRate,
            CancellationToken cancellationToken = default)
        {
            if (fee != null && feeRate != null)
                throw new ArgumentException("Parameters Fee and FeePrice cannot be used at the same time");

            // check if all outputs are available
            var availableOutputs = await GetAvailableOutputsAsync()
                .ConfigureAwait(false);

            if (outputs.Any(o => availableOutputs.FirstOrDefault(ao => ao.TxId == o.TxId && ao.Index == o.Index) == null))
            {
                return new MaxAmountEstimation
                {
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        description: string.Format(
                            Resources.OutputsAlreadySpent,
                            Currency),
                        details: string.Format(
                            Resources.OutputsAlreadySpentDetails,
                            Currency)) // currency code
                };
            }

            if (outputs == null || !outputs.Any())
                return new MaxAmountEstimation {
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        description: Resources.InsufficientFunds,
                        details: string.Format(
                            Resources.InsufficientFundsDetails,
                            0m,        // available
                            Currency)) // currency code
                };

            var availableInSatoshi = outputs.Sum(o => o.Value);

            if (fee != null)
            {
                var feeInSatoshi = Config.CoinToSatoshi(fee.Value);

                return new MaxAmountEstimation {
                    Amount = Config.SatoshiToCoin(Math.Max(availableInSatoshi - feeInSatoshi, 0)),
                    Fee    = Config.SatoshiToCoin(feeInSatoshi)
                };
            }

            if (feeRate == null)
            {
                feeRate = await Config
                    .GetFeeRateAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            var inputsToSign = outputs
                .Select(o => new BitcoinInputToSign { Output = o })
                .Where(i => i.SizeWithSignature() * feeRate < i.Output.Value); // skip outputs that are less than the fee for adding them

            availableInSatoshi = inputsToSign.Sum(i => i.Output.Value);

            var inputsSize = inputsToSign.Sum(i => i.SizeWithSignature());
            var witnessCount = outputs.Sum(o => o.IsSegWit ? 1 : 0);

            var destinationSize = to != null
                ? new BitcoinDestination { Script = BitcoinAddress.Create(to, Config.Network).ScriptPubKey }.Size()
                : BitcoinBasedConfig.LegacyTxOutputSize;

            var changeAddress = await GetFreeInternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            var (size, sizeWithChange) = BitcoinTransactionParams.CalculateTxSize(
                inputsCount: inputsToSign.Count(),
                inputsSize: inputsSize,
                outputsCount: 1,
                outputsSize: destinationSize,
                witnessCount: witnessCount,
                changeOutputSize: BitcoinTransactionParams.CalculateChangeOutputSize(changeAddress.Address, Config.Network));

            var estimatedFeeInSatoshi = (long)(feeRate * size);

            if (availableInSatoshi < estimatedFeeInSatoshi) // not enough funds for a tx with one output
                return new MaxAmountEstimation {
                    Amount = Config.SatoshiToCoin(availableInSatoshi - estimatedFeeInSatoshi),
                    Fee    = Config.SatoshiToCoin(estimatedFeeInSatoshi),
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        description: Resources.InsufficientFunds,
                        details: string.Format(Resources.InsufficientFundsToSendAmountDetails,
                            Config.SatoshiToCoin(estimatedFeeInSatoshi), // required
                            Currency,              // currency code
                            Config.SatoshiToCoin(availableInSatoshi)))   // available
                };

            return new MaxAmountEstimation
            {
                Amount = Config.SatoshiToCoin(availableInSatoshi - estimatedFeeInSatoshi),
                Fee    = Config.SatoshiToCoin(estimatedFeeInSatoshi)
            };
        }

        #endregion Common

        #region Balances

        public override async Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            var scanner = new BitcoinBasedWalletScanner(this);

            await scanner
                .UpdateBalanceAsync(skipUsed: false, cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var scanner = new BitcoinBasedWalletScanner(this);

            await scanner
                .UpdateBalanceAsync(address, cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion Balances

        #region Addresses

        public virtual async Task<WalletAddress> GetFreeInternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var lastActiveAddress = await LocalStorage
                .GetLastActiveWalletAddressAsync(
                    currency: Currency,
                    chain: Bip44.Internal,
                    keyType: CurrencyConfig.StandardKey)
                .ConfigureAwait(false);

            return await DivideAddressAsync(
                    account: Bip44.DefaultAccount,
                    chain: Bip44.Internal,
                    index: lastActiveAddress?.KeyIndex.Index + 1 ?? 0,
                    keyType: CurrencyConfig.StandardKey)
                .ConfigureAwait(false);
        }

        public async Task<WalletAddress> GetRefundAddressAsync(
            CancellationToken cancellationToken = default)
        {
            return await GetFreeInternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion Addresses

        #region Outputs

        public Task<IEnumerable<BitcoinBasedTxOutput>> GetAvailableOutputsAsync() =>
            LocalStorage.GetAvailableOutputsAsync(Currency);

        public Task<IEnumerable<BitcoinBasedTxOutput>> GetAvailableOutputsAsync(string address) =>
            LocalStorage.GetAvailableOutputsAsync(
                currency: Currency,
                address: address);

        public Task<IEnumerable<BitcoinBasedTxOutput>> GetOutputsAsync() =>
            LocalStorage.GetOutputsAsync(Currency);

        #endregion Outputs

        #region AddressResolver

        public Task<WalletAddress> GetAddressAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            return GetAddressAsync(address, cancellationToken);
        }

        #endregion AddressResolver

        #region Transactions

        public override async Task<IEnumerable<IBlockchainTransaction>> GetUnconfirmedTransactionsAsync(
            CancellationToken cancellationToken = default)
        {
            return await LocalStorage
                .GetUnconfirmedTransactionsAsync<BitcoinBasedTransaction>(Currency)
                .ConfigureAwait(false);
        }

        #endregion Transactions
    }
}