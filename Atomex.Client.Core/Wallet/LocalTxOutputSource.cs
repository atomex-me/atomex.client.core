using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin;
using Atomex.Core;
using Atomex.Wallet.BitcoinBased;

namespace Atomex.Wallet
{
    public class LocalTxOutputSource : ITxOutputSource
    {
        private readonly BitcoinBasedAccount _account;

        public LocalTxOutputSource(BitcoinBasedAccount account)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync(
            IEnumerable<WalletAddress> addresses)
        {
            var outputs = new List<BitcoinTxOutput>();

            foreach (var a in addresses)
            {
                var unspentOuts = await _account
                    .GetAvailableOutputsAsync(a.Address)
                    .ConfigureAwait(false);

                outputs.AddRange(unspentOuts);
            }

            return outputs;
        }

        public async Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync(
            IEnumerable<string> addresses)
        {
            var outputs = new List<BitcoinTxOutput>();

            foreach (var address in addresses)
            {
                var unspentOuts = await _account
                    .GetAvailableOutputsAsync(address)
                    .ConfigureAwait(false);

                outputs.AddRange(unspentOuts);
            }

            return outputs;
        }
    }
}