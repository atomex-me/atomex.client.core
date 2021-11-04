using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;

using Atomex.Common;

namespace Atomex.Blockchain.BitcoinBased
{
    public class BitcoinTransactionParams
    {
        public IEnumerable<BitcoinInputToSign> InputsToSign { get; set; }
        public IEnumerable<BitcoinDestination> Destinations { get; set; }
        public decimal FeeInSatoshi { get; set; }
        public decimal FeeRate { get; set; }
        public string ChangeAddress { get; set; }

        public static Task<BitcoinTransactionParams> SelectTransactionParamsAsync(
            IEnumerable<BitcoinInputToSign> availableInputs,
            IEnumerable<BitcoinDestination> destinations,
            string changeAddress,
            decimal feeRate,
            BitcoinBasedConfig currencyConfig,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                if (!availableInputs.Any())
                    return Task.FromResult<BitcoinTransactionParams>(null); // not enough funds

                // sort available outputs ascending
                var sortedInputs = availableInputs
                    .ToList()
                    .SortList((i1, i2) => i1.Output.Value.CompareTo(i2.Output.Value));

                var requiredInSatoshi = destinations.Sum(d => d.AmountInSatoshi);
                var outputsSize       = destinations.Sum(d => d.Size());
                var changeOutputSize  = CalculateChangeOutputSize(changeAddress, currencyConfig.Network);
                var feeInSatoshi      = 0m;

                // try to use one input
                if (sortedInputs.Last().Output.Value >= requiredInSatoshi)
                {
                    foreach (var input in sortedInputs)
                    {
                        // skip small inputs
                        if (input.Output.Value < requiredInSatoshi)
                            continue;

                        feeInSatoshi = CalculateFee(
                            inputsCount: 1,
                            inputsSize: input.SizeWithSignature(),
                            inputsInSatoshi: input.Output.Value,
                            outputsCount: destinations.Count(),
                            outputsSize: outputsSize,
                            witnessCount: input.Output.IsSegWit ? 1 : 0,
                            requiredInSatoshi: requiredInSatoshi,
                            requiredFeeRate: feeRate,
                            changeOutputSize: changeOutputSize,
                            dustInSatoshi: currencyConfig.GetDust());

                        if (feeInSatoshi <= 0)
                            continue;

                        return Task.FromResult(new BitcoinTransactionParams
                        {
                            InputsToSign  = new BitcoinInputToSign[] { input },
                            Destinations  = destinations,
                            FeeInSatoshi  = feeInSatoshi,
                            FeeRate       = feeRate,
                            ChangeAddress = changeAddress
                        });
                    }
                }

                var usedInputs     = new LinkedList<BitcoinInputToSign>();
                var usedInSatoshi  = 0m;
                var usedInputsSize = 0;
                var witnessCount   = 0;

                // try to use several inputs
                for (var i = 0; i < sortedInputs.Count; ++i)
                {
                    var input = sortedInputs[i];

                    usedInputs.AddLast(input);
                    usedInSatoshi += input.Output.Value;
                    usedInputsSize += input.SizeWithSignature();

                    if (input.Output.IsSegWit)
                        witnessCount++;

                    feeInSatoshi = CalculateFee(
                        inputsCount: usedInputs.Count(),
                        inputsSize: usedInputsSize,
                        inputsInSatoshi: usedInSatoshi,
                        outputsCount: destinations.Count(),
                        outputsSize: outputsSize,
                        witnessCount: witnessCount,
                        requiredInSatoshi: requiredInSatoshi,
                        requiredFeeRate: feeRate,
                        changeOutputSize: changeOutputSize,
                        dustInSatoshi: currencyConfig.GetDust());

                    if (feeInSatoshi <= 0)
                        continue;

                    break;
                }

                // insufficient funds
                if (feeInSatoshi <= 0)
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

                    var reducedFeeInSatoshi = CalculateFee(
                        inputsCount: usedInputs.Count() - i - 1,
                        inputsSize: usedInputsSize,
                        inputsInSatoshi: usedInSatoshi,
                        outputsCount: destinations.Count(),
                        outputsSize: outputsSize,
                        witnessCount: witnessCount,
                        requiredInSatoshi: requiredInSatoshi,
                        requiredFeeRate: feeRate,
                        changeOutputSize: changeOutputSize,
                        dustInSatoshi: currencyConfig.GetDust());

                    if (reducedFeeInSatoshi <= 0)
                        break;

                    feeInSatoshi = reducedFeeInSatoshi;
                    skip = i + 1;
                }

                return Task.FromResult(new BitcoinTransactionParams
                {
                    InputsToSign  = usedInputs.Skip(skip),
                    Destinations  = destinations,
                    FeeInSatoshi  = feeInSatoshi,
                    FeeRate       = feeRate,
                    ChangeAddress = changeAddress
                });

            }, cancellationToken);
        }


        public static (decimal size, decimal sizeWithChange) CalculateTxSize(
            int inputsCount,
            decimal inputsSize,
            int outputsCount,
            decimal outputsSize,
            int witnessCount,
            int changeOutputSize)
        {
            var size = 4                           // version
                + inputsCount.CompactSize()        // inputs count compact size
                + outputsCount.CompactSize()       // outputs count compact size (without change output)
                + 4                                // nlockTime
                + ((witnessCount > 0)              // segwit marker + witness count compact size
                    ? 0.5m + witnessCount.CompactSize() / 4
                    : 0)
                + inputsSize                       // tx inputs size
                + outputsSize;                     // tx outputs size

            var outputCountChangeDiff = (outputsCount + 1).CompactSize() - outputsCount.CompactSize();
            var sizeWithChange = size + changeOutputSize + outputCountChangeDiff;

            return (size, sizeWithChange);
        }

        public static decimal CalculateFee(
            int inputsCount,
            decimal inputsSize,
            decimal inputsInSatoshi,
            int outputsCount,
            decimal outputsSize,
            int witnessCount,
            decimal requiredInSatoshi,
            decimal requiredFeeRate,
            int changeOutputSize,
            decimal dustInSatoshi)
        {
            // inputs amount are insufficient for required amount
            if (inputsInSatoshi < requiredInSatoshi)
                return 0;

            // estimate tx size
            var (size, sizeWithChange) = CalculateTxSize(
                inputsCount: inputsCount,
                inputsSize: inputsSize,
                outputsCount: outputsCount,
                outputsSize: outputsSize,
                witnessCount: witnessCount,
                changeOutputSize: changeOutputSize);

            var feeInSatoshi = size * requiredFeeRate;

            // inputs amount are insufficient for required amount + fee
            if (inputsInSatoshi < requiredInSatoshi + feeInSatoshi)
                return 0;

            var changeInSatoshi = inputsInSatoshi - requiredInSatoshi - feeInSatoshi;

            if (changeInSatoshi == 0)
                return feeInSatoshi;

            var feeWithChangeInSatoshi = sizeWithChange * requiredFeeRate;

            // inputs amount are insufficient for required amount + fee
            if (inputsInSatoshi < requiredInSatoshi + feeWithChangeInSatoshi)
                return 0;

            changeInSatoshi = inputsInSatoshi - requiredInSatoshi - feeWithChangeInSatoshi;

            if (changeInSatoshi < dustInSatoshi)
                return feeWithChangeInSatoshi + changeInSatoshi;

            return feeWithChangeInSatoshi;
        }

        public static int CalculateChangeOutputSize(string changeAddress, Network network)
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