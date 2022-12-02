using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin;
using Atomex.Core;

namespace Atomex.Blockchain
{
    public class BlockchainTxOutputSource : ITxOutputSource
    {
        private readonly BitcoinBasedConfig _currency;

        public BlockchainTxOutputSource(BitcoinBasedConfig currency)
        {
            _currency = currency ?? throw new ArgumentNullException(nameof(currency));
        }

        public async Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync(
            IEnumerable<WalletAddress> addresses)
        {
            var outputs = new List<BitcoinTxOutput>();

            foreach (var a in addresses)
            {
                var outputsResult = await ((BitcoinBlockchainApi)_currency.BlockchainApi)
                    .GetUnspentOutputsAsync(a.Address)
                    .ConfigureAwait(false);

                if (outputsResult.HasError)
                    break; // todo: return error

                if (outputsResult.Value != null)
                    outputs.AddRange(outputsResult.Value);
            }

            return outputs;
        }

        public async Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync(
            IEnumerable<string> addresses)
        {
            var outputs = new List<BitcoinTxOutput>();

            foreach (var address in addresses)
            {
                var outputsResult = await ((BitcoinBlockchainApi)_currency.BlockchainApi)
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