using System.Threading;
using System.Threading.Tasks;
using Atomex.Common;

namespace Atomex.Blockchain.Abstract
{
    public interface ITokenBlockchainApi
    {
        /// <summary>
        /// Get token balance for <paramref name="address"/>
        /// </summary>
        /// <param name="address">Address</param>
        /// <param name="callingAddress">Calling address</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Balance if success, otherwise error</returns>
        Task<Result<decimal>> GetTokenBalanceAsync(
            string address,
            string callingAddress,
            SecureBytes securePublicKey,
            CancellationToken cancellationToken = default);

        Task<Result<decimal>> TryGetTokenBalanceAsync(
            string address,
            string callingAddress,
            SecureBytes securePublicKey,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);

        //Task<Result<decimal>> TryGetTokenBigMapBalanceAsync(
        //    string address,
        //    int pointer,
        //    int attempts = 10,
        //    int attemptsIntervalMs = 1000,
        //    CancellationToken cancellationToken = default);

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

        //Task<Result<decimal>> TryGetFa2BigMapAllowanceAsync(
        //    string holderAddress,
        //    string spenderAddress,
        //    int attempts = 3,
        //    int attemptsIntervalMs = 1000,
        //    CancellationToken cancellationToken = default);
    }
}