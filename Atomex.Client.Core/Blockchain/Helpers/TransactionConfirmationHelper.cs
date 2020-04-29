using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
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
                    .TryGetTransactionAsync(txId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (txResult != null && txResult.HasError)
                {
                    if (txResult.Error.Code == (int)HttpStatusCode.NotFound ||
                        txResult.Error.Code == (int)HttpStatusCode.GatewayTimeout ||
                        txResult.Error.Code == (int)HttpStatusCode.ServiceUnavailable ||
                        txResult.Error.Code == (int)HttpStatusCode.InternalServerError ||
                        txResult.Error.Code == HttpHelper.SslHandshakeFailed ||
                        txResult.Error.Code == Errors.RequestError)
                        return new ConfirmationCheckResult(false, null);

                    Log.Error("Error while get {@currency} transaction {@txId}. Code: {@code}. Description: {@desc}",
                        currency.Name,
                        txId,
                        txResult.Error.Code,
                        txResult.Error.Description);

                    return txResult.Error;
                }

                var tx = txResult.Value;

                if (tx != null && tx.State == BlockchainTransactionState.Failed)
                    return new ConfirmationCheckResult(false, tx);

                if (tx == null || tx.BlockInfo == null || tx.BlockInfo.Confirmations < NumberOfConfirmations)
                    return new ConfirmationCheckResult(false, null);

                return new ConfirmationCheckResult(true, tx);
            }
            catch (Exception e)
            {
                Log.Error(e, "Transaction confirmation check error");
                return new Error(Errors.InternalError, e.Message);
            }
        }
    }
}