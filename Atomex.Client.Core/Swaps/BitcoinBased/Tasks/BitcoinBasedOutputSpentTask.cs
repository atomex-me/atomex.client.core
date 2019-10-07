using System.Net;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Serilog;

namespace Atomex.Swaps.BitcoinBased.Tasks
{
    public class BitcoinBasedOutputSpentTask : BlockchainTask
    {
        public string OutputHash { get; set; }
        public uint OutputIndex { get; set; }
        public ITxPoint SpentPoint { get; set; }

        public override async Task<bool> CheckCompletion()
        {
            var asyncResult = await ((IInOutBlockchainApi)Currency.BlockchainApi)
                .IsTransactionOutputSpent(OutputHash, OutputIndex)
                .ConfigureAwait(false);

            if (asyncResult.HasError)
            {
                if (asyncResult.Error.Code == (int) HttpStatusCode.NotFound)
                    return false;

                Log.Error(
                    "Error while get spent point for {@hash}:{@index} with code {@code} and description {@description}",
                    OutputHash,
                    OutputIndex,
                    asyncResult.Error.Code,
                    asyncResult.Error.Description);

                return false;
            }

            if (asyncResult.Value == null)
                return false;

            SpentPoint = asyncResult.Value;
            CompleteHandler(this);
            return true;
        }
    }
}