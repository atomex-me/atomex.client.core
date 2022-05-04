using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Core;
using Atomex.Wallet.BitcoinBased;

namespace Atomex.Wallet
{
    public class LocalTxOutputSource : ITxOutputSource
    {
        private readonly BitcoinBasedAccount_OLD _account;

        public LocalTxOutputSource(BitcoinBasedAccount_OLD account)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(
            IEnumerable<WalletAddress_OLD> addresses)
        {
            var outputs = new List<ITxOutput>();

            foreach (var a in addresses)
            {
                var unspentOuts = await _account
                    .GetAvailableOutputsAsync(a.Address)
                    .ConfigureAwait(false);

                outputs.AddRange(unspentOuts);
            }

            return outputs;
        }

        public async Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(
            IEnumerable<string> addresses)
        {
            var outputs = new List<ITxOutput>();

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