using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomex.Abstract;
using Atomex.Services.Abstract;
using Atomex.TzktEvents;
using Atomex.Wallet.Abstract;
using Serilog;


namespace Atomex.Services.BalanceUpdaters
{
    public class TezosBalanceUpdater : IChainBalanceUpdater
    {
        private readonly IAccount _account;
        private readonly ICurrenciesProvider _currenciesProvider;
        private readonly ILogger _log;
        private readonly ITzktEventsClient _tzktEvents;
        private readonly IHdWalletScanner _walletScanner;

        private ISet<string> _tezosAddresses;


        public TezosBalanceUpdater(IAccount account, ICurrenciesProvider currenciesProvider, IHdWalletScanner walletScanner, ILogger log)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _currenciesProvider = currenciesProvider;
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _walletScanner = walletScanner ?? throw new ArgumentNullException(nameof(walletScanner));

            _tzktEvents = new TzktEventsClient(_log);
        }

        public async Task StartAsync()
        {
            if (_currenciesProvider == null)
            {
                throw new InvalidOperationException("Start TezosBalanceUpdater was called before CurrenciesProvider initialization");
            }

            try
            {
                var currency = _currenciesProvider
                    .GetCurrencies(_account.Network)
                    .Get<TezosConfig>(TezosConfig.Xtz);
                var baseUri = currency.BaseUri;

                await _tzktEvents.StartAsync(baseUri).ConfigureAwait(false);
                _tezosAddresses = await GetAddressesAsync().ConfigureAwait(false);

                await _tzktEvents
                    .NotifyOnAccountsAsync(_tezosAddresses, TezosBalanceUpdateHandler)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log.Error(e, "Error on starting TezosBalanceUpdater");
            }
        }

        public async Task StopAsync()
        {
            try
            {
                await _tzktEvents.StopAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log.Error(e, "Error on stopping TezosBalanceUpdater");
            }
        }


        private async Task<ISet<string>> GetAddressesAsync()
        {
            var account = _account.GetCurrencyAccount(TezosConfig.Xtz);
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

        private async void TezosBalanceUpdateHandler(string address)
        {
            try
            {
                await _walletScanner
                    .ScanAddressAsync(TezosConfig.Xtz, address)
                    .ConfigureAwait(false);

                var newAddresses = await GetAddressesAsync().ConfigureAwait(false);
                newAddresses.ExceptWith(_tezosAddresses);

                if (newAddresses.Any())
                {
                    Log.Information("TezosBalanceUpdater adds new addresses {@Addresses}", newAddresses);
                    await _tzktEvents
                        .NotifyOnAccountsAsync(newAddresses, TezosBalanceUpdateHandler)
                        .ConfigureAwait(false);

                    _tezosAddresses.UnionWith(newAddresses);
                }
            }
            catch (Exception e)
            {
                _log.Error(e, "Error on handling Tezos balance update");
            }
        }
    }
}
