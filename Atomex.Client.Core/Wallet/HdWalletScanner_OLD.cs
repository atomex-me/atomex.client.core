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
    public class HdWalletScanner_OLD : IHdWalletScanner_OLD
    {
        private IAccount_OLD Account { get; }

        public HdWalletScanner_OLD(IAccount_OLD account)
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

        private ICurrencyHdWalletScanner_OLD GetCurrencyScanner(string currency)
        {
            return currency switch
            {
                "BTC" => (ICurrencyHdWalletScanner_OLD)new BitcoinBasedWalletScanner_OLD(
                    Account.GetCurrencyAccount<BitcoinBasedAccount_OLD>(currency)),

                "LTC" => new BitcoinBasedWalletScanner_OLD(
                    Account.GetCurrencyAccount<BitcoinBasedAccount_OLD>(currency)),

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
                "FA12" or
                "FA2" => new TezosTokensScanner(
                    Account.GetCurrencyAccount<TezosAccount>(TezosConfig_OLD.Xtz)),

                _ => throw new NotSupportedException($"Currency {currency} not supported")
            };
        }
    }
}