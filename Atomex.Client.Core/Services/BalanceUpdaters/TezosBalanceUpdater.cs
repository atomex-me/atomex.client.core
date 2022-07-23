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
        private readonly ITzktEventsClient _tzkt;
        private readonly IWalletScanner _walletScanner;

        private ISet<string> _addresses;


        public TezosBalanceUpdater(IAccount account, ICurrenciesProvider currenciesProvider, IWalletScanner walletScanner, ITzktEventsClient tzkt, ILogger log)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _currenciesProvider = currenciesProvider;
            _walletScanner = walletScanner ?? throw new ArgumentNullException(nameof(walletScanner));
            _tzkt = tzkt ?? throw new ArgumentNullException(nameof(tzkt));
            _log = log ?? throw new ArgumentNullException(nameof(log));
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

                await _tzkt.StartAsync(baseUri).ConfigureAwait(false);
                _addresses = await GetAddressesAsync().ConfigureAwait(false);

                await _tzkt
                    .NotifyOnAccountsAsync(_addresses, BalanceUpdatedHandler)
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
                await _tzkt.StopAsync().ConfigureAwait(false);
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

        private async void BalanceUpdatedHandler(string address)
        {
            try
            {
                await _walletScanner
                    .ScanAddressAsync(TezosConfig.Xtz, address)
                    .ConfigureAwait(false);

                var newAddresses = await GetAddressesAsync().ConfigureAwait(false);
                newAddresses.ExceptWith(_addresses);

                if (newAddresses.Any())
                {
                    Log.Information("TezosBalanceUpdater adds new addresses {@Addresses}", newAddresses);
                    await _tzkt
                        .NotifyOnAccountsAsync(newAddresses, BalanceUpdatedHandler)
                        .ConfigureAwait(false);

                    _addresses.UnionWith(newAddresses);
                }
            }
            catch (Exception e)
            {
                _log.Error(e, "Error on handling Tezos balance update");
            }
        }
    }
}
