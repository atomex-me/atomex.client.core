using System;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Serilog;

namespace Atomex.Blockchain
{
    public class TransactionConfirmationCheckTask : BlockchainTask
    {
        private const int NumberOfConfirmations = 1;

        public string TxId { get; set; }
        public IBlockchainTransaction Tx { get; private set; }

        public override async Task<bool> CheckCompletion()
        {
            IBlockchainTransaction tx;

            try
            {
                tx = await Currency.BlockchainApi
                    .GetTransactionAsync(TxId)
                    .ConfigureAwait(false);

                if (tx == null)
                    return false;
            }
            catch (Exception e)
            {
                Log.Error(e, "Transaction confirmation check error");
                return false;
            }

            if (tx.BlockInfo == null || tx.BlockInfo.Confirmations < NumberOfConfirmations)
                return false;

            Tx = tx;

            CompleteHandler?.Invoke(this);
            return true;
        }
    }
}