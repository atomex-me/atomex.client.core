using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;

namespace Atomix.Wallet.CurrencyAccount
{
    public class TezosCurrencyAccount : CurrencyAccount
    {
        public TezosCurrencyAccount(
            Currency currency,
            IHdWallet wallet,
            ITransactionRepository transactionRepository)
            : base(currency, wallet, transactionRepository)
        {
        }

        public override Task<Error> SendAsync(
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public override Task<decimal> EstimateFeeAsync(
            decimal amount,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public override Task AddConfirmedTransactionAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public override Task AddUnconfirmedTransactionAsync(
            IBlockchainTransaction tx,
            string[] selfAddresses,
            bool notify = true,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public override Task<decimal> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public override Task<decimal> GetBalanceAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.FromResult(0m);
        }

        public override Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            decimal requiredAmount,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public override Task<bool> IsAddressHasOperationsAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }
    }
}