using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Ethereum.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Ethereum.Erc20
{
    public class Erc20ApiSettings
    {
    }

    public class Erc20Api : IErc20Api
    {
        public Erc20Api(
            Erc20ApiSettings settings,
            ILogger logger = null)
        {
        }

        public Task<(BigInteger balance, Error error)> GetErc20BalanceAsync(
            string address,
            string token,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<(IEnumerable<Erc20Transaction> txs, Error error)> GetErc20TransactionsAsync(
            string address,
            string token,
            DateTimeOffset fromTimeStamp,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<(long? count, Error error)> GetTransactionsCountAsync(
            string address,
            bool pending = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}