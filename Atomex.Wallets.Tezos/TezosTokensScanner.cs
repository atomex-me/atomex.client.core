using System;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallets.Tezos
{
    public class TezosTokensScanner : IWalletScanner
    {
        public Task<Error> UpdateAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Error> UpdateBalanceAsync(
            bool forceUpdate = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Error> UpdateBalanceAsync(
            int walletId,
            bool forceUpdate = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Error> UpdateAddressBalanceAsync(
            string address,
            string tokenContract,
            int tokenId = 0,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Error> ScanAsync(
            bool forceUpdate = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Error> ScanAsync(
            int walletId,
            bool forceUpdate = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}