using System.Collections.Generic;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Core.Entities;

namespace Atomex.Blockchain
{
    public class BlockchainTxOutputSource : ITxOutputSource
    {
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
            Currency currency,
            IEnumerable<string> addresses)
        {
            var outputs = new List<ITxOutput>();

            foreach (var address in addresses)
            {
                var outputsResult = await ((IInOutBlockchainApi)currency.BlockchainApi)
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