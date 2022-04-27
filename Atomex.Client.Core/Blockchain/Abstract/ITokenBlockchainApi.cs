using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;
using Atomex.Common.Memory;

namespace Atomex.Blockchain.Abstract
{
    public interface ITokenBlockchainApi
    {
        Task<Result<decimal>> GetFa12AllowanceAsync(
            string holderAddress,
            string spenderAddress,
            string callingAddress,
            SecureBytes securePublicKey,
            CancellationToken cancellationToken = default);

        Task<Result<decimal>> TryGetFa12AllowanceAsync(
            string holderAddress,
            string spenderAddress,
            string callingAddress,
            SecureBytes securePublicKey,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);
    }
}