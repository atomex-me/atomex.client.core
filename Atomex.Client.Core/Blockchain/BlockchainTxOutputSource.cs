using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Core;

namespace Atomex.Blockchain
{
    public class BlockchainTxOutputSource : ITxOutputSource
    {
        private readonly BitcoinBasedCurrency _currency;

        public BlockchainTxOutputSource(BitcoinBasedCurrency currency)
        {
            _currency = currency ?? throw new ArgumentNullException(nameof(currency));
        }

        public async Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(
            IEnumerable<WalletAddress> addresses)
        {
            var outputs = new List<ITxOutput>();

            foreach (var a in addresses)
            {
                var outputsResult = await ((IInOutBlockchainApi)a.Currency.BlockchainApi)
                    .GetUnspentOutputsAsync(a.Address)
                    .ConfigureAwait(false);

                if (outputsResult.HasError)
                    break; // todo: return error

                if (outputsResult.Value != null)
                    outputs.AddRange(outputsResult.Value);
            }

            return outputs;
        }

        public async Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(
            string currency,
            IEnumerable<string> addresses)
        {
            var outputs = new List<ITxOutput>();

            foreach (var address in addresses)
            {
                var outputsResult = await ((IInOutBlockchainApi)_currency.BlockchainApi)
                    .GetUnspentOutputsAsync(address)
                    .ConfigureAwait(false);

                if (outputsResult.HasError)
                    break; // todo: return error

                if (outputsResult.Value != null)
                    outputs.AddRange(outputsResult.Value);
            }

            return outputs;
        }
    }
}