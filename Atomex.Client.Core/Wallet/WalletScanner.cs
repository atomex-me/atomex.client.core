#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Tezos;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;
using Atomex.Wallet.Ethereum;
using Atomex.Wallet.Tezos;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallet
{
    public class WalletScanner : IWalletScanner
    {
        private readonly IAccount _account;
        private readonly ILogger? _logger;

        public WalletScanner(IAccount account, ILogger? logger = null)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _logger = logger;
        }

        public async Task ScanAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var scanTasks = _account
                    .Currencies
                    .Select(c => ScanAsync(c.Name, cancellationToken))
                    .ToArray();

                await Task
                    .WhenAll(scanTasks)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "Error while scan balance for all currencies");
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
                _logger?.LogError(e, "Error while scan balance for {@currency}", currency);
            }
        }

        public async Task UpdateBalanceAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var scanTasks = _account
                    .Currencies
                    .Select(c => UpdateBalanceAsync(c.Name, skipUsed, cancellationToken))
                    .ToArray();

                await Task
                    .WhenAll(scanTasks)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "Error while update balance for all currencies");
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
                _logger?.LogError(e, "Error while update balance for {@currency}", currency);
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
                _logger?.LogError(e, "Error while update balance for {@currency} address {@address}", currency, address);
            }
        }

        private ICurrencyWalletScanner GetCurrencyScanner(string currency)
        {
            return currency switch
            {
                "BTC" => new BitcoinBasedWalletScanner(
                    _account.GetCurrencyAccount<BitcoinBasedAccount>(currency)),
                "LTC" => new BitcoinBasedWalletScanner(
                    _account.GetCurrencyAccount<BitcoinBasedAccount>(currency)),
                "USDT" or
                "TBTC" or
                "WBTC" => new Erc20WalletScanner(
                    _account.GetCurrencyAccount<Erc20Account>(currency),
                    _account.GetCurrencyAccount<EthereumAccount>("ETH")),
                "ETH" => new EthereumWalletScanner(
                    _account.GetCurrencyAccount<EthereumAccount>(currency)),
                "XTZ" => new TezosWalletScanner(
                    account: _account.GetCurrencyAccount<TezosAccount>(currency),
                    logger: _logger),
                "TZBTC" or
                "KUSD" or
                "FA12" => new TezosTokensWalletScanner(
                    tezosAccount: _account.GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz),
                    tokenType: TezosHelper.Fa12,
                    logger: _logger),
                "USDT_XTZ" or
                "FA2" => new TezosTokensWalletScanner(
                    tezosAccount: _account.GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz),
                    tokenType: TezosHelper.Fa2,
                    logger: _logger),

                _ => throw new NotSupportedException($"Currency {currency} not supported")
            };
        }
    }
}