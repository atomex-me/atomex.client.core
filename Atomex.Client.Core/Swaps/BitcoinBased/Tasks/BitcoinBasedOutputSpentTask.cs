using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain.BitcoinBased
{
    public class BitcoinBasedOutputSpentTask : BlockchainTask
    {
        public string OutputHash { get; set; }
        public uint OutputIndex { get; set; }
        public ITxPoint SpentPoint { get; set; }

        public override async Task<bool> CheckCompletion()
        {
            var spentPoint = await ((IInOutBlockchainApi)Currency.BlockchainApi)
                .IsTransactionOutputSpent(OutputHash, OutputIndex)
                .ConfigureAwait(false);

            if (spentPoint == null)
                return false;

            SpentPoint = spentPoint;
            CompleteHandler(this);
            return true;
        }
    }
}