using System;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Serilog;

namespace Atomix.Blockchain
{
    public class TransactionConfirmedTask : BlockchainTask
    {
        public const int NumberOfConfirmations = 1;

        public string TxId { get; set; }
        public IBlockchainTransaction Tx { get; set; }

        public override async Task<bool> CheckCompletion()
        {
            IBlockchainTransaction tx;

            try
            {
                tx = await Currency.BlockchainApi
                    .GetTransactionAsync(TxId)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Transaction confirmation task error");
                return false;
            }

            if (tx == null || tx.BlockInfo == null || tx.BlockInfo.Confirmations < NumberOfConfirmations)
                return false;

            Tx = tx;

            CompleteHandler?.Invoke(this);
            return true;
        }
    }
}