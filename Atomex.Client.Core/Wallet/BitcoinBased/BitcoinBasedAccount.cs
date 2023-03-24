using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;
using NBitcoin;

using Atomex.Abstract;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin;
using Atomex.Common;
using Atomex.Wallet.Abstract;
using Atomex.Wallets.Bips;
using Atomex.Wallets;

namespace Atomex.Wallet.BitcoinBased
{
    public class BitcoinBasedAccount : CurrencyAccount, IEstimatable
    {
        public BitcoinBasedConfig Config => Currencies.Get<BitcoinBasedConfig>(Currency);

        public BitcoinBasedAccount(
            string currency,
            ICurrencies currencies,
            IHdWallet wallet,
            ILocalStorage localStorage)
                : base(currency, currencies, wallet, localStorage)
        {
        }

        #region Common

        public async Task<Result<string>> SendAsync(
            IEnumerable<BitcoinTxOutput> from,
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
                    message: $"Insufficient amount to send. Min non-dust amount {config.SatoshiToCoin(config.GetDust())}, actual {config.SatoshiToCoin(amountInSatoshi)}");

            from = from
                .SelectOutputsForAmount(requiredInSatoshi)
                .ToList();

            var availableInSatoshi = from.Sum(o => o.Value);

            if (!from.Any())
                return new Error(
                    code: Errors.InsufficientFunds,
                    message: $"Insufficient funds. Required {config.SatoshiToCoin(requiredInSatoshi)}, available {config.SatoshiToCoin(availableInSatoshi)}");

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
                            message: $"Change {config.SatoshiToCoin(changeInSatoshi)} can be definded by the network as dust and the transaction will be rejected");
                    case DustUsagePolicy.AddToDestination:
                        amountInSatoshi += changeInSatoshi;
                        break;
                    case DustUsagePolicy.AddToFee:
                        feeInSatoshi += changeInSatoshi;
                        break;
                    default:
                        return new Error(
                            code: Errors.InternalError,
                            message: $"Unknown dust usage policy value {dustUsagePolicy}");
                }
            }

            var tx = config.CreateTransaction(
                unspentOutputs: from,
                destinationAddress: to,
                changeAddress: changeAddress.Address,
                amount: (long)amountInSatoshi,
                fee: (long)feeInSatoshi);

            var signResult = await SignAsync(
                    tx: tx,
                    spentOutputs: from,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
                return new Error(
                    code: Errors.TransactionSigningError,
                    message: "Transaction signing error");

            if (!tx.Verify(from, out var errors, config.Network))
                return new Error(
                    code: Errors.TransactionVerificationError,
                    message: $"Transaction verification error: {string.Join(", ", errors.Select(e => e.Message))}");

            var (txId, error) = await config
                .GetBitcoinBlockchainApi()
                .BroadcastAsync(tx, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error.Value;

            if (txId == null)
                return new Error(
                    code: Errors.TransactionBroadcastError,
                    message: "Transaction id is null");

            Log.Debug("Transaction successfully sent with txId: {@id}", txId);

            await LocalStorage
                .UpsertTransactionAsync(
                    tx: tx,
                    notifyIfNewOrChanged: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return txId;
        }

        public async Task<bool> SignAsync(
            BitcoinTransaction tx,
            IEnumerable<BitcoinTxOutput> spentOutputs,
            CancellationToken cancellationToken = default)
        {
            try
            {
                foreach (var spentOutput in spentOutputs)
                {
                    var address = spentOutput.DestinationAddress(Config.Network);

                    var walletAddress = await GetAddressAsync(
                            address: address,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (walletAddress == null)
                        return false; // todo error?

                    var publicKey = Wallet.GetPublicKey(Config, walletAddress.KeyPath, walletAddress.KeyType);

                    var signatureHash = tx.GetSignatureHash(spentOutput, redeemScript: null, SigHash.All);

                    var signature = await Wallet
                        .SignHashAsync(
                            signatureHash,
                            walletAddress,
                            Config,
                            cancellationToken)
                        .ConfigureAwait(false);

                    var sigScript = spentOutput.Type switch
                    {
                        BitcoinOutputType.P2PKH => PayToPubkeyHashTemplate.Instance.GenerateScriptSig(
                            signature: new TransactionSignature(signature, SigHash.All),
                            publicKey: new PubKey(publicKey)),

                        BitcoinOutputType.P2WPKH => (Script)PayToWitPubKeyHashTemplate.Instance.GenerateWitScript(
                            signature: new TransactionSignature(signature, SigHash.All),
                            publicKey: new PubKey(publicKey)),

                        BitcoinOutputType.P2PK => PayToPubkeyTemplate.Instance.GenerateScriptSig(
                            signature: new TransactionSignature(signature, SigHash.All)),

                        _ => throw new NotSupportedException($"Type {spentOutput.Type} not supported")
                    };

                    tx.SetSignature(sigScript, spentOutput);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BitcoinBasedAccount] Sign error");
                return false;
            }
        }

        public async Task<decimal> EstimateFeeAsync(
            IEnumerable<BitcoinInputToSign> from,
            string changeTo,
            decimal amount,
            decimal feeRate,
            CancellationToken cancellationToken = default)
        {
            var txParams = await BitcoinTransactionParams
                .SelectTransactionParamsByFeeRateAsync(
                    availableInputs: from,
                    destinations: new (BigInteger AmountInSatoshi, int Size)[]
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

        public async Task<Result<decimal>> EstimateSwapPaymentFeeAsync(
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
            IEnumerable<BitcoinTxOutput> outputs,
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
                        message: string.Format(
                            Resources.OutputsAlreadySpent,
                            Currency)), // currency code
                    ErrorHint = string.Format(
                        Resources.OutputsAlreadySpentDetails,
                        Currency) // currency code
                };
            }

            if (outputs == null || !outputs.Any())
                return new MaxAmountEstimation {
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFunds),
                    ErrorHint = string.Format(
                        Resources.InsufficientFundsDetails,
                        0m,       // available
                        Currency) // currency code
                };

            var availableInSatoshi = outputs.SumBigIntegers(o => o.Value);

            if (fee != null)
            {
                var feeInSatoshi = Config.CoinToSatoshi(fee.Value);

                return new MaxAmountEstimation {
                    Amount = BigInteger.Max(availableInSatoshi - feeInSatoshi, 0),
                    Fee    = feeInSatoshi
                };
            }

            feeRate ??= await Config
                .GetFeeRateAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var inputsToSign = outputs
                .Select(o => new BitcoinInputToSign { Output = o })
                .Where(i => i.SizeWithSignature() * feeRate < i.Output.Value)
                .ToList(); // skip outputs that are less than the fee for adding them

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
                    Amount = availableInSatoshi - estimatedFeeInSatoshi,
                    Fee    = estimatedFeeInSatoshi,
                    Error = new Error(
                        code: Errors.InsufficientFunds,
                        message: Resources.InsufficientFunds),
                    ErrorHint = string.Format(Resources.InsufficientFundsToSendAmountDetails,
                        Config.SatoshiToCoin(estimatedFeeInSatoshi), // required
                        Currency,                                    // currency code
                        Config.SatoshiToCoin(availableInSatoshi))    // available
                };

            return new MaxAmountEstimation
            {
                Amount = availableInSatoshi - estimatedFeeInSatoshi,
                Fee    = estimatedFeeInSatoshi
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

        public override Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            return GetFreeAddressAsync(
                keyType: BitcoinBasedConfig.SegwitKey,
                chain: Bip44.External,
                cancellationToken: cancellationToken);
        }

        public virtual Task<WalletAddress> GetFreeInternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            return GetFreeAddressAsync(
                keyType: BitcoinBasedConfig.SegwitKey,
                chain: Bip44.Internal,
                cancellationToken: cancellationToken);
        }

        public async Task<WalletAddress> GetRefundAddressAsync(
            CancellationToken cancellationToken = default)
        {
            //return GetFreeAddressAsync(
            //    keyType: CurrencyConfig.StandardKey, // temporary use Standard keys for refund
            //    chain: Bip44.Internal,
            //    cancellationToken: cancellationToken);
            return await GetFreeInternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion Addresses

        #region Outputs

        public Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync() =>
            LocalStorage.GetAvailableOutputsAsync(Currency);

        public Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync(string address) =>
            LocalStorage.GetAvailableOutputsAsync(
                currency: Currency,
                address: address);

        public Task<IEnumerable<BitcoinTxOutput>> GetOutputsAsync() =>
            LocalStorage.GetOutputsAsync(Currency);

        #endregion Outputs

        #region Transactions

        public override async Task<IEnumerable<ITransaction>> GetUnconfirmedTransactionsAsync(
            CancellationToken cancellationToken = default)
        {
            return await LocalStorage
                .GetUnconfirmedTransactionsAsync<BitcoinTransaction>(Currency)
                .ConfigureAwait(false);
        }

        public override async Task ResolveTransactionsMetadataAsync(
            IEnumerable<ITransaction> txs,
            CancellationToken cancellationToken = default)
        {
            var resolvedMetadata = new List<ITransactionMetadata>();

            foreach (var tx in txs.Cast<BitcoinTransaction>())
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
                    (BitcoinTransaction)tx,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<TransactionMetadata> ResolveTransactionMetadataAsync(
            BitcoinTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var result = new TransactionMetadata
            {
                Id = tx.Id,
                Currency = tx.Currency
            };

            var isSwapTx = false;
            BigInteger outputAmount = 0;
            BigInteger inputAmount = 0;

            foreach (var i in tx.Inputs)
            {
                var localInput = await LocalStorage
                    .GetOutputAsync(
                        currency: Currency,
                        txId: i.PreviousOutput.Hash,
                        index: i.PreviousOutput.Index,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (localInput != null)
                {
                    // sent value
                    outputAmount += localInput.Value;
                }
                else if (i.IsRefund())
                {
                    result.Type |= TransactionType.SwapRefund;
                    isSwapTx = true;
                }
                else if (i.IsRedeem())
                {
                    result.Type |= TransactionType.SwapRedeem;
                    isSwapTx = true;
                }
            }

            foreach (var o in tx.Outputs)
            {
                var address = o.DestinationAddress(Config.Network);

                if (address == null)
                    continue;

                if (o.IsPayToScript)
                {
                    // pay to script (is it swap payment?)
                    var swap = await LocalStorage
                        .GetSwapByPaymentTxIdAsync(tx.Id, cancellationToken)
                        .ConfigureAwait(false);

                    if (swap != null)
                    {
                        result.Type |= TransactionType.SwapPayment;
                        isSwapTx = true;
                    }
                }
                else
                {
                    var localAddress = await GetAddressAsync(address, cancellationToken)
                        .ConfigureAwait(false);

                    if (localAddress != null)
                        inputAmount += o.Value;
                }
            }

            if (outputAmount > 0)
            {
                result.Type |= TransactionType.Output;
                result.Amount -= outputAmount;
            }

            if (inputAmount > 0)
            {
                result.Type |= TransactionType.Input;
                result.Amount += inputAmount;
            }

            if (outputAmount > 0 || isSwapTx)
            {
                result.Fee += tx.ResolvedFee;
                result.Amount += tx.ResolvedFee; // net amount without fees
            }

            return result;
        }

        #endregion Transactions
    }
}