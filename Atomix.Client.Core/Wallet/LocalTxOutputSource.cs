using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;

namespace Atomix.Wallet
{
    public class LocalTxOutputSource : ITxOutputSource
    {
        private readonly IAccount _account;

        public LocalTxOutputSource(IAccount account)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(IEnumerable<WalletAddress> addresses)
        {
            var outputs = new List<ITxOutput>();

            foreach (var a in addresses)
            {
                var unspentOuts = await _account
                    .GetUnspentOutputsAsync(a.Currency, a.Address)
                    .ConfigureAwait(false);

                outputs.AddRange(unspentOuts);
            }

            return outputs;
        }
    }
}