using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;

namespace Atomix.Blockchain
{
    public class TransactionConfirmedTask : BlockchainTask
    {
        public const int NumberOfConfirmations = 1;

        public string TxId { get; set; }
        public IBlockchainTransaction Tx { get; set; }

        public override async Task<bool> CheckCompletion()
        {
            var tx = await Currency.BlockchainApi
                .GetTransactionAsync(TxId)
                .ConfigureAwait(false);

            if (tx.BlockInfo == null || tx.BlockInfo.Confirmations < NumberOfConfirmations)
                return false;

            Tx = tx;

            CompleteHandler?.Invoke(this);
            return true;
        }
    }
}