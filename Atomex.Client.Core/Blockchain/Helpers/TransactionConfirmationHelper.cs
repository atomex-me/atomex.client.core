using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Blockchain.Helpers
{
    public class ConfirmationCheckResult
    {
        public bool IsConfirmed { get; }
        public ITransaction Transaction { get; }

        public ConfirmationCheckResult(bool isConfirmed, ITransaction tx)
        {
            IsConfirmed = isConfirmed;
            Transaction = tx;
        }
    }

    [Obsolete("Will be removed")]
    public static class TransactionConfirmationHelper
    {
        private const int NumberOfConfirmations = 1;

        public static async Task<Result<ConfirmationCheckResult>> IsTransactionConfirmed(
            this CurrencyConfig currency,
            string txId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (tx, error) = await currency
                    .GetBlockchainApi()
                    .GetTransactionAsync(txId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                {
                    if (error.Value.Code == (int)HttpStatusCode.NotFound ||
                        error.Value.Code == (int)HttpStatusCode.GatewayTimeout ||
                        error.Value.Code == (int)HttpStatusCode.ServiceUnavailable ||
                        error.Value.Code == (int)HttpStatusCode.InternalServerError ||
                        error.Value.Code == HttpHelper.SslHandshakeFailed ||
                        error.Value.Code == Errors.RequestError)
                        return new ConfirmationCheckResult(false, null);

                    Log.Error("Error while get {@currency} transaction {@txId}. Code: {@code}. Message: {@message}",
                        currency.Name,
                        txId,
                        error.Value.Code,
                        error.Value.Message);

                    return error;
                }

                if (tx != null && tx.Status == TransactionStatus.Failed)
                    return new ConfirmationCheckResult(false, tx);

                if (tx == null || tx.Confirmations < NumberOfConfirmations)
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