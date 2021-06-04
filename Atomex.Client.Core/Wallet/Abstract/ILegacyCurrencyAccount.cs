using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface ILegacyCurrencyAccount : ICurrencyAccount
    {
        #region Common

        Task<Error> SendAsync(
            IEnumerable<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool useDefaultFee = false,
            CancellationToken cancellationToken = default);

        Task<Error> SendAsync(
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool useDefaultFee = false,
            CancellationToken cancellationToken = default);

        Task<decimal?> EstimateFeeAsync(
            string to,
            decimal amount,
            BlockchainTransactionType type,
            decimal fee = 0,
            decimal feePrice = 0,
            CancellationToken cancellationToken = default);

        Task<(decimal, decimal, decimal)> EstimateMaxAmountToSendAsync(
            string to,
            BlockchainTransactionType type,
            decimal fee = 0,
            decimal feePrice = 0,
            bool reserve = false,
            CancellationToken cancellationToken = default);

        //Task<decimal> EstimateMaxFeeAsync(
        //    string to,
        //    decimal amount,
        //    BlockchainTransactionType type,
        //    CancellationToken cancellationToken = default);

        #endregion Common

        #region Addresses

        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string toAddress,
            decimal amount,
            decimal fee,
            decimal feePrice,
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default);

        Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default);

        //Task<IEnumerable<SelectedWalletAddress>> SelectUnspentAddressesAsync(
        //    IList<WalletAddress> from,
        //    string to,
        //    decimal amount,
        //    decimal fee,
        //    decimal feePrice,
        //    FeeUsagePolicy feeUsagePolicy,
        //    AddressUsagePolicy addressUsagePolicy,
        //    BlockchainTransactionType transactionType,
        //    CancellationToken cancellationToken = default);

        #endregion Addresses
    }
}