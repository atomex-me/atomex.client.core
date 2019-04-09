using System;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;
using Atomix.Wallet.BitcoinBased;
using Atomix.Wallet.Ethereum;
using Atomix.Wallet.Tezos;

namespace Atomix.Wallet
{
    public class HdWalletScanner : IHdWalletScanner
    {
        public const int DefaultInternalLookAhead = 3;
        public const int DefaultExternalLookAhead = 3;

        public int InternalLookAhead { get; set; } = DefaultInternalLookAhead;
        public int ExternalLookAhead { get; set; } = DefaultExternalLookAhead;
        public IAccount Account { get; }

        public HdWalletScanner(IAccount account)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            foreach (var currency in Account.Currencies)
                if (currency.IsTransactionsAvailable)
                    await ScanAsync(currency, skipUsed, cancellationToken)
                        .ConfigureAwait(false);
        }

        public Task ScanAsync(
            Currency currency,
            bool skipUsed = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ICurrencyHdWalletScanner scanner = null;

            if (currency is BitcoinBasedCurrency)
                scanner = new BitcoinBasedWalletScanner(currency, Account);
            else if (currency is Atomix.Ethereum)
                scanner = new EthereumWalletScanner(Account);
            else if (currency is Atomix.Tezos)
                scanner = new TezosWalletScanner(Account);
            else
                throw new NotSupportedException($"Currency {currency.Name} not supported");

            return scanner.ScanAsync(skipUsed, cancellationToken);
        }
    }
}