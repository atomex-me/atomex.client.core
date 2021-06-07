using System;
using System.Threading;
using System.Threading.Tasks;

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
                foreach (var currency in Account.Currencies)
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

                "USDT" => new Erc20WalletScanner(
                    Account.GetCurrencyAccount<Erc20Account>(currency),
                    Account.GetCurrencyAccount<EthereumAccount>("ETH")),

                "TBTC" => new Erc20WalletScanner(
                    Account.GetCurrencyAccount<Erc20Account>(currency),
                    Account.GetCurrencyAccount<EthereumAccount>("ETH")),

                "WBTC" => new Erc20WalletScanner(
                    Account.GetCurrencyAccount<Erc20Account>(currency),
                    Account.GetCurrencyAccount<EthereumAccount>("ETH")),

                "ETH" => new EthereumWalletScanner(
                    Account.GetCurrencyAccount<EthereumAccount>(currency)),

                "XTZ" => new TezosWalletScanner(
                    Account.GetCurrencyAccount<TezosAccount>(currency)),

                "TZBTC" => new Fa12WalletScanner(
                    Account.GetCurrencyAccount<Fa12Account>(currency), 
                    Account.GetCurrencyAccount<TezosAccount>("XTZ")),

                "KUSD" => new Fa12WalletScanner(
                    Account.GetCurrencyAccount<Fa12Account>(currency),
                    Account.GetCurrencyAccount<TezosAccount>("XTZ")),

                "FA2" => new Fa2WalletScanner(
                    Account.GetCurrencyAccount<Fa2Account>(currency),
                    Account.GetCurrencyAccount<TezosAccount>("XTZ")),

                _ => throw new NotSupportedException($"Currency {currency} not supported")
            };
        }
    }
}