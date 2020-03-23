using System;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;
using Atomex.Wallet.Ethereum;
using Atomex.Wallet.Tezos;
using Serilog;

namespace Atomex.Wallet
{
    public class HdWalletScanner : IHdWalletScanner
    {
        private IAccount Account { get; }

        public HdWalletScanner(IAccount account)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                foreach (var currency in Account.Currencies)
                    if (currency.IsTransactionsAvailable)
                        await ScanAsync(currency.Name, skipUsed, cancellationToken)
                            .ConfigureAwait(false);
            }, cancellationToken);
        }

        public Task ScanAsync(
            string currency,
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                await GetCurrencyScanner(currency)
                    .ScanAsync(skipUsed, cancellationToken)
                    .ConfigureAwait(false);
            }, cancellationToken);
        }

        public Task ScanFreeAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                foreach (var currency in Account.Currencies)
                {
                    var walletAddress = await Account
                        .GetFreeExternalAddressAsync(currency.Name, cancellationToken)
                        .ConfigureAwait(false);

                    try
                    {
                        await GetCurrencyScanner(currency.Name)
                            .ScanAsync(walletAddress.Address, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Free address scan error for currency: {@currency}", currency.Name);
                    }
                }
            }, cancellationToken);
        }

        public async Task ScanAddressAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await GetCurrencyScanner(currency)
                    .ScanAsync(address, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Console.WriteLine("Address scan error for currency {@currency} and address {@address}", currency, address);
            }
        }

        private ICurrencyHdWalletScanner GetCurrencyScanner(string currency)
        {
            return currency switch
            {
                "BTC" => (ICurrencyHdWalletScanner)new BitcoinBasedWalletScanner(Account.GetCurrencyAccount<BitcoinBasedAccount>(currency)),
                "LTC" => (ICurrencyHdWalletScanner)new BitcoinBasedWalletScanner(Account.GetCurrencyAccount<BitcoinBasedAccount>(currency)),
                "USDT" => (ICurrencyHdWalletScanner)new ERC20WalletScanner(
                    Account.GetCurrencyAccount<ERC20Account>(currency),
                    Account.GetCurrencyAccount<EthereumAccount>("ETH")),
                //"USDC" => (ICurrencyHdWalletScanner)new ERC20WalletScanner(Account.GetCurrencyAccount<ERC20Account>(currency)),
                "ETH" => (ICurrencyHdWalletScanner)new EthereumWalletScanner(Account.GetCurrencyAccount<EthereumAccount>(currency)),
                "XTZ" => (ICurrencyHdWalletScanner)new TezosWalletScanner(Account.GetCurrencyAccount<TezosAccount>(currency)),
                "TZBTC" => (ICurrencyHdWalletScanner)new TezosWalletScanner(Account.GetCurrencyAccount<FA12Account>(currency)),
                "FA12" => (ICurrencyHdWalletScanner)new TezosWalletScanner(Account.GetCurrencyAccount<FA12Account>(currency)),
                _ => throw new NotSupportedException($"Currency {currency} not supported")
            };
        }
    }
}