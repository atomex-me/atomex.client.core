using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomex.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.EthereumTokens;
using Atomex.Services.Abstract;
using Atomex.Wallet.Abstract;
using Serilog;


namespace Atomex.Services.BalanceUpdaters
{
    public class Erc20BalanceUpdater : IChainBalanceUpdater
    {
        private readonly IAccount _account;
        private readonly ICurrenciesProvider _currenciesProvider;
        private readonly ILogger _log;
        private readonly IHdWalletScanner _walletScanner;

        private readonly IList<IErc20Notifier> _notifiers = new List<IErc20Notifier>();
        private ISet<string> _addresses;


        public Erc20BalanceUpdater(IAccount account, ICurrenciesProvider currenciesProvider, IHdWalletScanner walletScanner, ILogger log)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _currenciesProvider = currenciesProvider;
            _walletScanner = walletScanner ?? throw new ArgumentNullException(nameof(walletScanner));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task StartAsync()
        {
            if (_currenciesProvider == null)
            {
                throw new InvalidOperationException("Start ERC20BalanceUpdater was called before CurrenciesProvider initialization");
            }

            try
            {
                var ethCurrency = _currenciesProvider
                    .GetCurrencies(_account.Network)
                    .Get<EthereumConfig>(EthereumConfig.Eth);
                // var baseUri = ethCurrency.InfuraWsApi;
                var baseUri = ethCurrency.InfuraApi.Replace("https", "wss").Replace("v3", "ws/v3");

                foreach (var currency in _account.Currencies)
                {
                    if (currency is Erc20Config erc20Currency)
                    {
                        _notifiers.Add(new Erc20Notifier(baseUri, erc20Currency, _log));
                    }
                }

                await Task.WhenAll(_notifiers.Select(n => n.StartAsync())).ConfigureAwait(false);
                _addresses = await GetAddressesAsync().ConfigureAwait(false);

                await Task.WhenAll(
                    _notifiers.Select(n => n.SubscribeOnEventsAsync(_addresses, BalanceUpdatedHandler)))
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log.Error(e, "Error on starting ERC20BalanceUpdater");
            }
        }

        public async Task StopAsync()
        {
            try
            {
                await Task.WhenAll(_notifiers.Select(n => n.StopAsync())).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log.Error(e, "Error on stopping ERC20BalanceUpdater");
            }
        }


        private async Task<ISet<string>> GetAddressesAsync()
        {
            var account = _account.GetCurrencyAccount(EthereumConfig.Eth);
            var addresses = await account
                .GetAddressesAsync()
                .ConfigureAwait(false);

            var freeAddress = await account
                .GetFreeExternalAddressAsync()
                .ConfigureAwait(false);

            return addresses.Concat(new[] { freeAddress })
                .Select(wa => wa.Address)
                .ToHashSet();
        }

        private async void BalanceUpdatedHandler(string currency, string address)
        {
            try
            {
                await _walletScanner
                    .ScanAddressAsync(currency, address)
                    .ConfigureAwait(false);

                var newAddresses = await GetAddressesAsync().ConfigureAwait(false);
                newAddresses.ExceptWith(_addresses);

                if (newAddresses.Any())
                {
                    Log.Information("ERC20BalanceUpdater adds new addresses {@Addresses}", newAddresses);
                    await Task.WhenAll(
                            _notifiers.Select(n => n.SubscribeOnEventsAsync(newAddresses, BalanceUpdatedHandler)))
                        .ConfigureAwait(false);
                    _addresses.UnionWith(newAddresses);
                }
            }
            catch (Exception e)
            {
                _log.Error(e, "Error on handling ERC20 balance update");
            }
        }
    }
}
