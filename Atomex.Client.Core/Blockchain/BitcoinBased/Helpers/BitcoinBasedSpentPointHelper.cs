using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Serilog;

namespace Atomex.Blockchain.BitcoinBased.Helpers
{
    public static class BitcoinBasedSpentPointHelper
    {
        public static async Task<Result<ITxPoint>> GetSpentPointAsync(
            this CurrencyConfig currency, 
            string hash,
            uint index,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (!(currency.BlockchainApi is BitcoinBasedBlockchainApi_OLD api))
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
                        return new Result<ITxPoint>((ITxPoint)null);

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