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
            this Currency currency, 
            string hash,
            uint index,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await((IInOutBlockchainApi)currency.BlockchainApi)
                    .IsTransactionOutputSpent(hash, index, cancellationToken)
                    .ConfigureAwait(false);

                if (result.HasError)
                {
                    if (result.Error.Code == (int)HttpStatusCode.NotFound)
                        return new Result<ITxPoint>((ITxPoint)null);

                    Log.Error(
                        "Error while get spent point for {@currency} tx output {@hash}:{@index}. Code: {@code}. Description {@desc}.",
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
                    hash,
                    index);

                return new Error(Errors.InternalError, e.Message);
            }
        }  
    }
}