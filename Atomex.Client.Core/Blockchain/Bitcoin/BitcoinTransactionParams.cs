using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;

using Atomex.Common;
using Atomex.Blockchain.Bitcoin.Common;
using Atomex.Wallet.BitcoinBased;

namespace Atomex.Blockchain.Bitcoin
{
    public class BitcoinTransactionParams
    {
        public IEnumerable<BitcoinInputToSign> InputsToSign { get; set; }
        public decimal Size { get; set; }
        public BigInteger FeeInSatoshi { get; set; }
        public decimal FeeRate { get; set; }
        public string ChangeAddress { get; set; }
        public bool UseChangeAddress { get; set; }

        public BigInteger InputInSatoshi => InputsToSign.Sum(i => i.Output.Value);
        public BigInteger ChangeInSatoshi => InputInSatoshi - FeeInSatoshi;

        public static async Task<BitcoinTransactionParams> SelectTransactionParamsByFeeRateAsync(
            IEnumerable<BitcoinTxOutput> availableOutputs,
            string to,
            decimal amount,
            decimal feeRate,
            BitcoinBasedAccount account,
            CancellationToken cancellationToken = default)
        {
            var config = account.Config;

            var freeInternalAddress = await account
                .GetFreeInternalAddressAsync()
                .ConfigureAwait(false);

            return await SelectTransactionParamsByFeeRateAsync(
                    availableInputs: availableOutputs.Select(o => new BitcoinInputToSign { Output = o }),
                    destinations: new BitcoinDestination[]
                    {
                        new BitcoinDestination
                        {
                            AmountInSatoshi = config.CoinToSatoshi(amount),
                            Script = BitcoinAddress.Create(to, config.Network).ScriptPubKey,
                        }
                    },
                    changeAddress: freeInternalAddress.Address,
                    feeRate: feeRate,
                    currencyConfig: config,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public static Task<BitcoinTransactionParams> SelectTransactionParamsByFeeRateAsync(
            IEnumerable<BitcoinInputToSign> availableInputs,
            IEnumerable<BitcoinDestination> destinations,
            string changeAddress,
            decimal feeRate,
            BitcoinBasedConfig currencyConfig,
            CancellationToken cancellationToken = default)
        {
            return SelectTransactionParamsByFeeRateAsync(
                availableInputs: availableInputs,
                destinations: destinations.Select(d => (d.AmountInSatoshi, d.Size())),
                changeAddress: changeAddress,
                feeRate: feeRate,
                currencyConfig: currencyConfig,
                cancellationToken: cancellationToken);
        }

        public static Task<BitcoinTransactionParams> SelectTransactionParamsByFeeRateAsync(
            IEnumerable<BitcoinInputToSign> availableInputs,
            IEnumerable<(BigInteger AmountInSatoshi, int Size)> destinations,
            string changeAddress,
            decimal feeRate,
            BitcoinBasedConfig currencyConfig,
            CancellationToken cancellationToken = default)
        {
            if (!availableInputs.Any())
                return Task.FromResult<BitcoinTransactionParams>(null); // not enough funds

            // sort available outputs ascending
            var sortedInputs = availableInputs
                .ToList()
                .SortList((i1, i2) => i1.Output.Value.CompareTo(i2.Output.Value));

            var requiredAmountInSatoshi = destinations.SumBigIntegers(d => d.AmountInSatoshi);
            var outputsSize             = destinations.Sum(d => d.Size);
            var changeOutputSize        = CalculateChangeOutputSize(changeAddress, currencyConfig.Network);

            // try to use one input
            if (sortedInputs.Last().Output.Value >= requiredAmountInSatoshi)
            {
                foreach (var input in sortedInputs)
                {
                    // skip small inputs
                    if (input.Output.Value < requiredAmountInSatoshi)
                        continue;

                    var (size, sizeWithChange) = CalculateTxSize(
                        inputsCount: 1,
                        inputsSize: input.SizeWithSignature(),
                        outputsCount: destinations.Count(),
                        outputsSize: outputsSize,
                        witnessCount: input.Output.IsSegWit ? 1 : 0,
                        changeOutputSize: changeOutputSize);

                    var (calculatedFeeInSatoshi, useChangeAddress) = CalculateFee(
                        size: size,
                        sizeWithChange: sizeWithChange,
                        inputsInSatoshi: input.Output.Value,
                        requiredAmountInSatoshi: requiredAmountInSatoshi,
                        requiredFeeRate: feeRate,
                        dustInSatoshi: currencyConfig.GetDust());

                    if (calculatedFeeInSatoshi <= 0) // insufficient funds
                        continue;

                    var transactionSize = useChangeAddress
                        ? sizeWithChange
                        : size;

                    return Task.FromResult(new BitcoinTransactionParams
                    {
                        InputsToSign     = new BitcoinInputToSign[] { input },
                        Size             = transactionSize,
                        FeeInSatoshi     = calculatedFeeInSatoshi,
                        FeeRate          = calculatedFeeInSatoshi.Divide(transactionSize),
                        ChangeAddress    = changeAddress,
                        UseChangeAddress = useChangeAddress
                    });
                }
            }

            var usedInputs           = new LinkedList<BitcoinInputToSign>();
            BigInteger usedInSatoshi = 0;
            var usedInputsSize       = 0;
            var witnessCount         = 0;

            var resultTransactionSize     = 0;
            BigInteger resultFeeInSatoshi = 0;
            var resultChangeAddressUsed   = false;

            // skip inputs that are less than the fee for adding them
            sortedInputs = sortedInputs
                .Where(i => i.SizeWithSignature() * feeRate < i.Output.Value)
                .ToList();

            // try to use several inputs
            for (var i = 0; i < sortedInputs.Count; ++i)
            {
                var input = sortedInputs[i];

                usedInSatoshi += input.Output.Value;
                usedInputs.AddLast(input);
                usedInputsSize += input.SizeWithSignature();

                if (input.Output.IsSegWit)
                    witnessCount++;

                if (usedInSatoshi < requiredAmountInSatoshi) // insufficient funds
                    continue;

                var (size, sizeWithChange) = CalculateTxSize(
                    inputsCount: usedInputs.Count(),
                    inputsSize: usedInputsSize,
                    outputsCount: destinations.Count(),
                    outputsSize: outputsSize,
                    witnessCount: witnessCount,
                    changeOutputSize: changeOutputSize);

                var (calculatedFeeInSatoshi, useChangeAddress) = CalculateFee(
                    size: size,
                    sizeWithChange: sizeWithChange,
                    inputsInSatoshi: usedInSatoshi,
                    requiredAmountInSatoshi: requiredAmountInSatoshi,
                    requiredFeeRate: feeRate,
                    dustInSatoshi: currencyConfig.GetDust());

                if (calculatedFeeInSatoshi <= 0) // insufficient funds
                    continue;

                resultFeeInSatoshi      = calculatedFeeInSatoshi;
                resultChangeAddressUsed = useChangeAddress;
                resultTransactionSize   = useChangeAddress ? sizeWithChange : size;
                break;
            }

            // insufficient funds
            if (resultFeeInSatoshi <= 0)
                return Task.FromResult<BitcoinTransactionParams>(null);

            var skip = 0;

            // try to reduce inputs count
            for (var i = 0; i < usedInputs.Count - 1; ++i)
            {
                var input = sortedInputs[i];

                usedInSatoshi -= input.Output.Value;
                usedInputsSize -= input.SizeWithSignature();

                if (input.Output.IsSegWit)
                    witnessCount--;

                if (usedInSatoshi < requiredAmountInSatoshi) // insufficient funds
                    break;

                var (size, sizeWithChange) = CalculateTxSize(
                    inputsCount: usedInputs.Count() - i - 1,
                    inputsSize: usedInputsSize,
                    outputsCount: destinations.Count(),
                    outputsSize: outputsSize,
                    witnessCount: witnessCount,
                    changeOutputSize: changeOutputSize);

                var (reducedFeeInSatoshi, useChangeAddress) = CalculateFee(
                    size: size,
                    sizeWithChange: sizeWithChange,
                    inputsInSatoshi: usedInSatoshi,
                    requiredAmountInSatoshi: requiredAmountInSatoshi,
                    requiredFeeRate: feeRate,
                    dustInSatoshi: currencyConfig.GetDust());

                if (reducedFeeInSatoshi <= 0) // insufficient funds
                    break;

                resultFeeInSatoshi      = reducedFeeInSatoshi;
                resultChangeAddressUsed = useChangeAddress;
                resultTransactionSize   = useChangeAddress ? sizeWithChange : size;

                skip = i + 1;
            }

            return Task.FromResult(new BitcoinTransactionParams
            {
                InputsToSign     = usedInputs.Skip(skip),
                Size             = resultTransactionSize,
                FeeInSatoshi     = resultFeeInSatoshi,
                FeeRate          = resultFeeInSatoshi.Divide(resultTransactionSize),
                ChangeAddress    = changeAddress,
                UseChangeAddress = resultChangeAddressUsed
            });
        }

        public static async Task<BitcoinTransactionParams> SelectTransactionParamsByFeeAsync(
            IEnumerable<BitcoinTxOutput> availableOutputs,
            string to,
            decimal amount,
            decimal fee,
            BitcoinBasedAccount account,
            CancellationToken cancellationToken = default)
        {
            var config = account.Config;

            var freeInternalAddress = await account
                .GetFreeInternalAddressAsync()
                .ConfigureAwait(false);

            return await SelectTransactionParamsByFeeAsync(
                    availableInputs: availableOutputs.Select(o => new BitcoinInputToSign { Output = o }),
                    destinations: new BitcoinDestination[]
                    {
                        new BitcoinDestination
                        {
                            AmountInSatoshi = config.CoinToSatoshi(amount),
                            Script = BitcoinAddress.Create(to, config.Network).ScriptPubKey,
                        }
                    },
                    changeAddress: freeInternalAddress.Address,
                    feeInSatoshi: config.CoinToSatoshi(fee),
                    currencyConfig: config,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public static Task<BitcoinTransactionParams> SelectTransactionParamsByFeeAsync(
            IEnumerable<BitcoinInputToSign> availableInputs,
            IEnumerable<BitcoinDestination> destinations,
            string changeAddress,
            BigInteger feeInSatoshi,
            BitcoinBasedConfig currencyConfig,
            CancellationToken cancellationToken = default)
        {
            if (!availableInputs.Any())
                return Task.FromResult<BitcoinTransactionParams>(null); // not enough funds

            // sort available outputs ascending
            var sortedInputs = availableInputs
                .ToList()
                .SortList((i1, i2) => i1.Output.Value.CompareTo(i2.Output.Value));

            var requiredAmountInSatoshi = destinations.SumBigIntegers(d => d.AmountInSatoshi);
            var outputsSize             = destinations.Sum(d => d.Size());
            var changeOutputSize        = CalculateChangeOutputSize(changeAddress, currencyConfig.Network);

            var resultTransactionSize     = 0;
            BigInteger resultFeeInSatoshi = 0;
            var resultUseChangeAddress    = false;

            // try to use one input
            if (sortedInputs.Last().Output.Value >= requiredAmountInSatoshi)
            {
                foreach (var input in sortedInputs)
                {
                    // skip small inputs
                    if (input.Output.Value < requiredAmountInSatoshi + feeInSatoshi)
                        continue;

                    var changeInSatoshi = input.Output.Value - requiredAmountInSatoshi - feeInSatoshi;
                    resultUseChangeAddress = changeInSatoshi >= currencyConfig.GetDust();

                    var (size, sizeWithChange) = CalculateTxSize(
                        inputsCount: 1,
                        inputsSize: input.SizeWithSignature(),
                        outputsCount: destinations.Count(),
                        outputsSize: outputsSize,
                        witnessCount: input.Output.IsSegWit ? 1 : 0,
                        changeOutputSize: changeOutputSize);

                    resultTransactionSize = resultUseChangeAddress
                        ? sizeWithChange
                        : size;

                    resultFeeInSatoshi = resultUseChangeAddress
                        ? feeInSatoshi
                        : feeInSatoshi + changeInSatoshi;

                    return Task.FromResult(new BitcoinTransactionParams
                    {
                        InputsToSign     = new BitcoinInputToSign[] { input },
                        Size             = resultTransactionSize,
                        FeeInSatoshi     = resultFeeInSatoshi,
                        FeeRate          = resultFeeInSatoshi.Divide(resultTransactionSize),
                        ChangeAddress    = changeAddress,
                        UseChangeAddress = resultUseChangeAddress
                    });
                }
            }

            var usedInputs           = new LinkedList<BitcoinInputToSign>();
            BigInteger usedInSatoshi = 0;
            var usedInputsSize       = 0;
            var witnessCount         = 0;
            var success              = false;

            // try to use several inputs
            for (var i = 0; i < sortedInputs.Count; ++i)
            {
                var input = sortedInputs[i];

                usedInSatoshi += input.Output.Value;
                usedInputs.AddLast(input);
                usedInputsSize += input.SizeWithSignature();

                if (input.Output.IsSegWit)
                    witnessCount++;

                if (usedInSatoshi < requiredAmountInSatoshi + feeInSatoshi)
                    continue;

                var changeInSatoshi = usedInSatoshi - requiredAmountInSatoshi - feeInSatoshi;
                resultUseChangeAddress = changeInSatoshi >= currencyConfig.GetDust();

                var (size, sizeWithChange) = CalculateTxSize(
                    inputsCount: usedInputs.Count(),
                    inputsSize: usedInputsSize,
                    outputsCount: destinations.Count(),
                    outputsSize: outputsSize,
                    witnessCount: witnessCount,
                    changeOutputSize: changeOutputSize);

                resultTransactionSize = resultUseChangeAddress
                    ? sizeWithChange
                    : size;

                resultFeeInSatoshi = resultUseChangeAddress
                    ? feeInSatoshi
                    : feeInSatoshi + changeInSatoshi;

                success = true;
                break;
            }

            // insufficient funds
            if (!success)
                return Task.FromResult<BitcoinTransactionParams>(null);

            var skip = 0;

            // try to reduce inputs count
            for (var i = 0; i < usedInputs.Count - 1; ++i)
            {
                var input = sortedInputs[i];

                usedInSatoshi -= input.Output.Value;
                usedInputsSize -= input.SizeWithSignature();

                if (input.Output.IsSegWit)
                    witnessCount--;

                if (usedInSatoshi < requiredAmountInSatoshi + feeInSatoshi)
                    break;

                var changeInSatoshi = usedInSatoshi - requiredAmountInSatoshi - feeInSatoshi;
                resultUseChangeAddress = changeInSatoshi >= currencyConfig.GetDust();

                var (size, sizeWithChange) = CalculateTxSize(
                    inputsCount: usedInputs.Count() - i - 1,
                    inputsSize: usedInputsSize,
                    outputsCount: destinations.Count(),
                    outputsSize: outputsSize,
                    witnessCount: witnessCount,
                    changeOutputSize: changeOutputSize);

                resultTransactionSize = resultUseChangeAddress
                    ? sizeWithChange
                    : size;

                resultFeeInSatoshi = resultUseChangeAddress
                    ? feeInSatoshi
                    : feeInSatoshi + changeInSatoshi;

                skip = i + 1;
            }

            return Task.FromResult(new BitcoinTransactionParams
            {
                InputsToSign     = usedInputs.Skip(skip),
                Size             = resultTransactionSize,
                FeeInSatoshi     = resultFeeInSatoshi,
                FeeRate          = resultFeeInSatoshi.Divide(resultTransactionSize),
                ChangeAddress    = changeAddress,
                UseChangeAddress = resultUseChangeAddress
            });
        }

        public static (int size, int sizeWithChange) CalculateTxSize(
            int inputsCount,
            int inputsSize,
            int outputsCount,
            int outputsSize,
            int witnessCount,
            int changeOutputSize)
        {
            var size = 4                           // version
                + inputsCount.CompactSize()        // inputs count compact size
                + outputsCount.CompactSize()       // outputs count compact size (without change output)
                + 4                                // nlockTime
                + ((witnessCount > 0)              // segwit marker + witness count compact size
                    ? 0.5 + witnessCount.CompactSize() / 4
                    : 0)
                + inputsSize                       // tx inputs size
                + outputsSize;                     // tx outputs size

            var outputCountChangeDiff = (outputsCount + 1).CompactSize() - outputsCount.CompactSize();
            var sizeWithChange = size + changeOutputSize + outputCountChangeDiff;

            return ((int)Math.Ceiling(size), (int)Math.Ceiling(sizeWithChange));
        }
            
        public static (BigInteger feeInSatoshi, bool useChangeAddress) CalculateFee(
            int size,
            int sizeWithChange,
            BigInteger inputsInSatoshi,
            BigInteger requiredAmountInSatoshi,
            decimal requiredFeeRate,
            BigInteger dustInSatoshi)
        {
            if (sizeWithChange < size)
                throw new ArgumentException("Size with change must be greater or equal to size");

            // inputs amount are insufficient for required amount
            if (inputsInSatoshi < requiredAmountInSatoshi)
                return (feeInSatoshi: 0, useChangeAddress: false);

            var feeInSatoshi = new BigInteger(size * requiredFeeRate);

            // inputs amount are insufficient for required amount + fee
            if (inputsInSatoshi < requiredAmountInSatoshi + feeInSatoshi)
                return (feeInSatoshi: 0, useChangeAddress: false);

            var changeInSatoshi = inputsInSatoshi - requiredAmountInSatoshi - feeInSatoshi;

            if (changeInSatoshi == 0)
                return (feeInSatoshi, useChangeAddress: false);

            // if the change is less than dust, then add it to the fee
            if (changeInSatoshi < dustInSatoshi)
                return (feeInSatoshi: feeInSatoshi + changeInSatoshi, useChangeAddress: false);

            var feeWithChangeInSatoshi = new BigInteger(sizeWithChange * requiredFeeRate);

            var newChangeInSatoshi = inputsInSatoshi - requiredAmountInSatoshi - feeWithChangeInSatoshi; // or changeInSatoshi - (feeWithChangeInSatoshi - feeInSatoshi)

            // if the change minus the fee difference is less than dust, then it doesn't make sense to add output with change.
            // equal to (changeInSatoshi < dustInSatoshi + (feeWithChangeInSatoshi - feeInSatoshi))
            if (newChangeInSatoshi < dustInSatoshi)
                return (feeInSatoshi: feeInSatoshi + changeInSatoshi, useChangeAddress: false);

            // inputs amount are insufficient for required amount + fee
            if (inputsInSatoshi < requiredAmountInSatoshi + feeWithChangeInSatoshi)
                return (feeInSatoshi: 0, useChangeAddress: false);

            return (feeInSatoshi: feeWithChangeInSatoshi, useChangeAddress: true);
        }

        public static int CalculateChangeOutputSize(
            string changeAddress,
            Network network)
        {
            var changeOutputSize = BitcoinAddress
                .Create(changeAddress, network)
                .ScriptPubKey
                .ToBytes()
                .Length;

            return changeOutputSize + changeOutputSize.CompactSize();
        }
    }
}