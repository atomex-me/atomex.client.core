using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;
using Atomex.Wallet.Ethereum;
using Atomex.Wallet.Tezos;

namespace Atomex.Wallet
{
    public class WalletScanner : IWalletScanner
    {
        private IAccount Account { get; }

        public WalletScanner(IAccount account)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public Task ScanAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var scanTasks = Account.Currencies
                        .Select(c => ScanAsync(c.Name, cancellationToken))
                        .ToArray();

                    await Task.WhenAll(scanTasks)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error while scanning HdWallet for all currencies");
                }

            }, cancellationToken);
        }

        public Task ScanAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    await GetCurrencyScanner(currency)
                        .ScanAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error while scanning HdWallet for {Currency} currency", currency);
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
                Log.Error(e, "Address scan error for currency {@currency} and address {@address}", currency, address);
            }
        }

        private ICurrencyWalletScanner GetCurrencyScanner(string currency)
        {
            return currency switch
            {
                "BTC" => new BitcoinBasedWalletScanner(
                    Account.GetCurrencyAccount<BitcoinBasedAccount>(currency)),

                "LTC" => new BitcoinBasedWalletScanner(
                    Account.GetCurrencyAccount<BitcoinBasedAccount>(currency)),

                "USDT" or
                "TBTC" or
                "WBTC" => new Erc20WalletScanner(
                    Account.GetCurrencyAccount<Erc20Account>(currency),
                    Account.GetCurrencyAccount<EthereumAccount>("ETH")),

                "ETH" => new EthereumWalletScanner(
                    Account.GetCurrencyAccount<EthereumAccount>(currency)),

                "XTZ" => new TezosWalletScanner(
                    Account.GetCurrencyAccount<TezosAccount>(currency)),

                "TZBTC" or
                "KUSD" or
                "USDT_XTZ" or
                "FA12" or
                "FA2" => new TezosTokensWalletScanner(
                    Account.GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz)),

                _ => throw new NotSupportedException($"Currency {currency} not supported")
            };
        }
    }
}