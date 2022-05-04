using System.Collections.Generic;
using System.Linq;

using Atomex.Blockchain.Bitcoin;

namespace Atomex.Wallets.Bitcoin
{
    public class BitcoinOutputsLocker
    {
        private readonly HashSet<string> _lockedOutputs;

        public BitcoinOutputsLocker()
        {
            _lockedOutputs = new HashSet<string>();
        }

        public bool TryLock(IEnumerable<BitcoinTxOutput> outputs)
        {
            lock (_lockedOutputs)
            {
                foreach (var output in outputs)
                    if (_lockedOutputs.Contains($"{output.Coin.Outpoint.Hash}:{output.Coin.Outpoint.N}"))
                        return false;

                foreach (var output in outputs)
                    _lockedOutputs.Add($"{output.Coin.Outpoint.Hash}:{output.Coin.Outpoint.N}");
            }

            return true;
        }

        public void Unlock(IEnumerable<BitcoinTxOutput> outputs)
        {
            lock (_lockedOutputs)
            {
                foreach (var output in outputs)
                    _lockedOutputs.Remove($"{output.Coin.Outpoint.Hash}:{output.Coin.Outpoint.N}");
            }
        }

        public IEnumerable<string> LockedOutputs()
        {
            lock (_lockedOutputs)
            {
                return _lockedOutputs.ToList();
            }
        }
    }
}