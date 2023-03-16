using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Common;

namespace Atomex.Blockchain.Bitcoin.Helpers
{
    public static class BitcoinSpentPointHelper
    {
        public static async Task<Result<BitcoinTxPoint>> GetSpentPointAsync(
            this BitcoinBasedConfig currencyConfig, 
            string hash,
            uint index,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (spentPoint, error) = await currencyConfig
                    .GetBitcoinBlockchainApi()
                    .IsTransactionOutputSpent(hash, index, cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                {
                    if (error.Value.Code == (int)HttpStatusCode.NotFound)
                        return new Result<BitcoinTxPoint> { Value = null };

                    Log.Error(
                        "Error while get spent point for {@currency} tx output {@hash}:{@index}. Code: {@code}. Message {@desc}.",
                        currencyConfig.Name,
                        hash,
                        index,
                        error.Value.Code,
                        error.Value.Message);
                }

                return spentPoint;
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while get spent point for {@currency} tx output {@hash}:{@index}.",
                    currencyConfig.Name,
                    hash,
                    index);

                return new Error(Errors.InternalError, e.Message);
            }
        }  
    }
}