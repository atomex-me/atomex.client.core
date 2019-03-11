using System.Collections.Generic;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Core.Entities;

namespace Atomix.Blockchain
{
    public class BlockchainTxOutputSource : ITxOutputSource
    {
        public async Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(
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
    }
}