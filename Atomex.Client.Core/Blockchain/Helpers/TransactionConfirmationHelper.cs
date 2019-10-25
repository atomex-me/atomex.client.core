using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Serilog;

namespace Atomex.Blockchain.Helpers
{
    public class ConfirmationCheckResult
    {
        public bool IsConfirmed { get; }
        public IBlockchainTransaction Transaction { get; }

        public ConfirmationCheckResult(bool isConfirmed, IBlockchainTransaction tx)
        {
            IsConfirmed = isConfirmed;
            Transaction = tx;
        }
    }

    public static class TransactionConfirmationHelper

    {
        private const int NumberOfConfirmations = 1;

        public static Task<Result<ConfirmationCheckResult>> IsTransactionConfirmed(
            this IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            return IsTransactionConfirmed(
                currency: tx.Currency,
                txId: tx.Id,
                cancellationToken: cancellationToken);
        }

        public static async Task<Result<ConfirmationCheckResult>> IsTransactionConfirmed(
            this Currency currency,
            string txId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var txResult = await currency.BlockchainApi
                    .GetTransactionAsync(txId, cancellationToken)
                    .ConfigureAwait(false);

                if (txResult.HasError)
                {
                    if (txResult.Error.Code == (int) HttpStatusCode.NotFound)
                        return new Result<ConfirmationCheckResult>(new ConfirmationCheckResult(false, null));

                    Log.Error("Error while get {@currency} transaction {@txId}. Code: {@code}. Description: {@desc}",
                        currency.Name,
                        txId,
                        txResult.Error.Code, 
                        txResult.Error.Description);

                    return new Result<ConfirmationCheckResult>(txResult.Error);
                }

                var tx = txResult.Value;

                if (tx == null || tx.BlockInfo == null || tx.BlockInfo.Confirmations < NumberOfConfirmations)
                    return new Result<ConfirmationCheckResult>(new ConfirmationCheckResult(false, null));

                return new Result<ConfirmationCheckResult>(new ConfirmationCheckResult(true, tx));
            }
            catch (Exception e)
            {
                Log.Error(e, "Transaction confirmation check error");
                return new Result<ConfirmationCheckResult>(new Error(Errors.InternalError, e.Message));
            }
        }
    }
}