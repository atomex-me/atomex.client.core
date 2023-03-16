using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Tezos.Common;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos.Abstract
{
    public interface ITezosApi
    {
        Task<Result<IEnumerable<TezosOperation>>> GetOperationsByAddressAsync(
            string address,
            DateTimeParameter? timeStamp = null,
            CancellationToken cancellationToken = default);

        Task<Result<bool>> IsRevealedAsync(
            string address,
            CancellationToken cancellationToken = default);
    }
}