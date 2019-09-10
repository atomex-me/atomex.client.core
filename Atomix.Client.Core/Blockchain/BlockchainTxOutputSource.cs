using System.Collections.Generic;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Core.Entities;

namespace Atomix.Blockchain
{
    public class BlockchainTxOutputSource : ITxOutputSource
    {
        public async Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(
            IEnumerable<WalletAddress> addresses)
        {
            var outputs = new List<ITxOutput>();

            foreach (var a in addresses)
            {
                var unspentOuts = await ((IInOutBlockchainApi)a.Currency.BlockchainApi)
                    .GetUnspentOutputsAsync(a.Address)
                    .ConfigureAwait(false);

                outputs.AddRange(unspentOuts);
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
                var unspentOuts = await ((IInOutBlockchainApi)currency.BlockchainApi)
                    .GetUnspentOutputsAsync(address)
                    .ConfigureAwait(false);

                outputs.AddRange(unspentOuts);
            }

            return outputs;
        }
    }
}