using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using Atomex.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Services.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex.Services.BalanceUpdaters
{
    public class EthereumBalanceUpdater : IChainBalanceUpdater
    {
        private readonly IAccount _account;
        private readonly ICurrenciesProvider _currenciesProvider;
        private readonly ILogger _log;
        private IEthereumNotifier _notifier;
        private readonly IWalletScanner _walletScanner;

        private ISet<string> _addresses;

        public EthereumBalanceUpdater(IAccount account, ICurrenciesProvider currenciesProvider, IWalletScanner walletScanner, ILogger log)
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
                throw new InvalidOperationException("Start EthereumBalanceUpdater was called before CurrenciesProvider initialization");
            }

            try
            {
                var currency = _currenciesProvider
                    .GetCurrencies(_account.Network)
                    .Get<EthereumConfig>(EthereumConfig.Eth);
                var baseUri = currency.BlockchainApiBaseUri;

                _notifier = new EthereumNotifier(baseUri, _log);

                await _notifier.StartAsync().ConfigureAwait(false);
                _addresses = await GetAddressesAsync().ConfigureAwait(false);

                _notifier.SubscribeOnBalanceUpdate(_addresses, BalanceUpdatedHandler);
            }
            catch (Exception e)
            {
                _log.LogError(e, "Error on starting EthereumBalanceUpdater");
            }
        }

        public async Task StopAsync()
        {
            try
            {
                await _notifier.StopAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log.LogError(e, "Error on stopping EthereumBalanceUpdater");
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

        private async void BalanceUpdatedHandler(string address)
        {
            try
            {
                await _walletScanner
                    .ScanAddressAsync(EthereumConfig.Eth, address)
                    .ConfigureAwait(false);

                var newAddresses = await GetAddressesAsync().ConfigureAwait(false);
                newAddresses.ExceptWith(_addresses);

                if (newAddresses.Any())
                {
                    _log.LogInformation("EthereumBalanceUpdater adds new addresses {@Addresses}", newAddresses);
                    _notifier.SubscribeOnBalanceUpdate(newAddresses, BalanceUpdatedHandler);
                    _addresses.UnionWith(newAddresses);
                }
            }
            catch (Exception e)
            {
                _log.LogError(e, "Error on handling Ethereum balance update");
            }
        }
    }
}