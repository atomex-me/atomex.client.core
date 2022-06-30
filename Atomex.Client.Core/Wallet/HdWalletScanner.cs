using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Core;
using Serilog;

using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;
using Atomex.Wallet.Ethereum;
using Atomex.Wallet.Tezos;

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
                try
                {
                    var scanTasks = Account.Currencies.Select(c => ScanAsync(c.Name, skipUsed, cancellationToken)).ToArray();
                    await Task.WhenAll(scanTasks).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error while scanning HdWallet for all currencies");
                }
            }, cancellationToken);
        }

        public Task ScanAsync(
            string currency,
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    await GetCurrencyScanner(currency)
                        .ScanAsync(skipUsed, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error while scanning HdWallet for {Currency} currency", currency);
                }
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
                        Log.Error(e, "Free address scan error for currency: {@currency}", currency.Name);
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
                Log.Error(e, "Address scan error for currency {@currency} and address {@address}", currency, address);
            }
        }

        private ICurrencyHdWalletScanner GetCurrencyScanner(string currency)
        {
            return currency switch
            {
                "BTC" => (ICurrencyHdWalletScanner)new BitcoinBasedWalletScanner(
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
                "FA2" => new TezosTokensScanner(
                    Account.GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz)),

                _ => throw new NotSupportedException($"Currency {currency} not supported")
            };
        }
    }
}