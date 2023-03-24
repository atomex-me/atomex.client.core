using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;
using Atomex.Wallet.Ethereum;
using Atomex.Wallet.Tezos;
using Atomex.Blockchain.Tezos;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallet
{
    public class WalletScanner : IWalletScanner
    {
        private IAccount Account { get; }

        public WalletScanner(IAccount account)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task ScanAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var scanTasks = Account
                    .Currencies
                    .Select(c => ScanAsync(c.Name, cancellationToken))
                    .ToArray();

                await Task
                    .WhenAll(scanTasks)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while scanning HdWallet for all currencies");
            }
        }

        public async Task ScanAsync(
            string currency,
            CancellationToken cancellationToken = default)
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
        }

        public async Task UpdateBalanceAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var scanTasks = Account
                    .Currencies
                    .Select(c => UpdateBalanceAsync(c.Name, skipUsed, cancellationToken))
                    .ToArray();

                await Task
                    .WhenAll(scanTasks)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while update balance for all currencies");
            }
        }

        public async Task UpdateBalanceAsync(
            string currency,
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await GetCurrencyScanner(currency)
                    .UpdateBalanceAsync(skipUsed, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while update balance for {Currency} currency", currency);
            }
        }

        public async Task UpdateBalanceAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await GetCurrencyScanner(currency)
                    .UpdateBalanceAsync(address, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Address balance update error for currency {@currency} and address {@address}", currency, address);
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
                "FA12" => new TezosTokensWalletScanner(
                    tezosAccount: Account.GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz),
                    tokenType: TezosHelper.Fa12),
                "USDT_XTZ" or
                "FA2" => new TezosTokensWalletScanner(
                    tezosAccount: Account.GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz),
                    tokenType: TezosHelper.Fa2),

                _ => throw new NotSupportedException($"Currency {currency} not supported")
            };
        }
    }
}