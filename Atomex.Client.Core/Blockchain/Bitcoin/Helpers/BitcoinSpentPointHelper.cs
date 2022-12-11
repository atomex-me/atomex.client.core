using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Serilog;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Blockchain.Bitcoin.Helpers
{
    public static class BitcoinSpentPointHelper
    {
        public static async Task<Result<BitcoinTxPoint>> GetSpentPointAsync(
            this CurrencyConfig currency, 
            string hash,
            uint index,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (!(currency.BlockchainApi is BitcoinBlockchainApi api))
                {
                    Log.Error("Api is null for currency {@currency}", currency.Name);
                    return new Error(Errors.InternalError, $"Api is null for currency {currency.Name}");
                }

                var result = await api
                    .IsTransactionOutputSpent(hash, index, cancellationToken)
                    .ConfigureAwait(false);

                if (result.HasError)
                {
                    if (result.Error.Code == (int)HttpStatusCode.NotFound)
                        return new Result<BitcoinTxPoint>((BitcoinTxPoint)null);

                    Log.Error(
                        "Error while get spent point for {@currency} tx output {@hash}:{@index}. Code: {@code}. Description {@desc}.",
                        currency.Name,
                        hash,
                        index,
                        result.Error.Code,
                        result.Error.Description);
                }

                return result;
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while get spent point for {@currency} tx output {@hash}:{@index}.",
                    currency.Name,
                    hash,
                    index);

                return new Error(Errors.InternalError, e.Message);
            }
        }  
    }
}