using System.Threading;
using System.Threading.Tasks;

namespace Atomix.Blockchain.Abstract
{
    public interface IBlockchainApi
    {
        Task<IBlockchainTransaction> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<string> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken));

        //Task<bool> IsTransactionConfirmed(
        //    string txId,
        //    CancellationToken cancellationToken = default(CancellationToken));   

        //Task<ConfidenceInformation> GetConfidenceAsync(
        //    string txId,
        //    CancellationToken cancellationToken = default(CancellationToken));

        //Task<long> GetBalanceAsync(
        //    string address,
        //    CancellationToken cancellationToken = default(CancellationToken));
    }
}