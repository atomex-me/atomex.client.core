using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Core.Entities;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet
{
    public class LocalTxOutputSource : ITxOutputSource
    {
        private readonly IAccount _account;

        public LocalTxOutputSource(IAccount account)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(
            IEnumerable<WalletAddress> addresses)
        {
            var outputs = new List<ITxOutput>();

            foreach (var a in addresses)
            {
                var unspentOuts = await _account
                    .GetAvailableOutputsAsync(a.Currency, a.Address)
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
                var unspentOuts = await _account
                    .GetAvailableOutputsAsync(currency, address)
                    .ConfigureAwait(false);

                outputs.AddRange(unspentOuts);
            }

            return outputs;
        }
    }
}