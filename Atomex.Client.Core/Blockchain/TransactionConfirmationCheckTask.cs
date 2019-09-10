using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Serilog;

namespace Atomex.Blockchain
{
    public class TransactionConfirmationCheckTask : BlockchainTask
    {
        private const int NumberOfConfirmations = 1;

        public string TxId { get; set; }
        public IEnumerable<IBlockchainTransaction> Transactions { get; private set; }

        public override async Task<bool> CheckCompletion()
        {
            IEnumerable<IBlockchainTransaction> txs;

            try
            {
                txs = await Currency.BlockchainApi
                    .GetTransactionsByIdAsync(TxId)
                    .ConfigureAwait(false);

                if (txs == null)
                    return false;
            }
            catch (Exception e)
            {
                Log.Error(e, "Transaction confirmation check error");
                return false;
            }

            if (txs == null || !txs.Any())
                return false;

            var firstTx = txs.First();
            if (firstTx.BlockInfo == null || firstTx.BlockInfo.Confirmations < NumberOfConfirmations)
                return false;

            Transactions = txs;

            CompleteHandler?.Invoke(this);
            return true;
        }
    }
}