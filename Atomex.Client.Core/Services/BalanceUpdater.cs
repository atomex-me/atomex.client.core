﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Abstract;
using Atomex.Services.Abstract;
using Atomex.TzktEvents;
using Atomex.Wallet;
using Atomex.Wallet.Abstract;
using Serilog;


namespace Atomex.Services
{
    public class BalanceUpdater : IBalanceUpdater
    {
        private readonly IAccount _account;
        private readonly ICurrenciesProvider _currenciesProvider;
        private readonly ILogger _log;
        private readonly ITzktEventsClient _tzktEvents;
        private readonly IHdWalletScanner _walletScanner;

        private CancellationTokenSource _cts;
        private bool _isRunning;

        private ISet<string> _tezosAddresses;

        public BalanceUpdater(IAccount account, ICurrenciesProvider currenciesProvider, ILogger log)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _currenciesProvider = currenciesProvider;
            _log = log ?? throw new ArgumentNullException(nameof(log));

            _tzktEvents = new TzktEventsClient(_log);
            _walletScanner = new HdWalletScanner(_account);
        }

        public void Start()
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("BalanceUpdater already running");
            }

            _isRunning = true;
            _cts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                try
                {
                    await StartTezosBalanceUpdater().ConfigureAwait(false);

                    _log.Information("BalanceUpdater successfully started");
                }
                catch (OperationCanceledException)
                {
                    _log.Debug("BalanceUpdater canceled");
                }
                catch (Exception e)
                {
                    _log.Error(e, "Unconfirmed BalanceUpdater error");
                }

            }, _cts.Token);
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await StopTezosBalanceUpdater().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _log.Error(e, "Error while stopping BalanceUpdater");
                }
            });

            _cts.Cancel();
            _isRunning = false;
            _log.Information("BalanceUpdater stopped");
        }


        #region Tezos

        private async Task StartTezosBalanceUpdater()
        {
            if (_currenciesProvider == null)
            {
                throw new InvalidOperationException("StartTezosBalanceUpdater was called before CurrenciesProvider initialization");
            }

            try
            {
                var currency = _currenciesProvider
                    .GetCurrencies(_account.Network)
                    .Get<TezosConfig>(TezosConfig.Xtz);
                var baseUri = currency.BaseUri;

                await _tzktEvents.StartAsync(baseUri).ConfigureAwait(false);

                var account = _account.GetCurrencyAccount(currency.Name);
                _tezosAddresses = await GetAddressesAsync().ConfigureAwait(false);
                
                await _tzktEvents
                    .NotifyOnAccountsAsync(_tezosAddresses, TezosBalanceUpdateHandler)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log.Error(e, "Error on starting Tezos balance updater");
            }
        }

        private async Task<ISet<string>> GetAddressesAsync()
        {
            var account = _account.GetCurrencyAccount(TezosConfig.Xtz);
            var addresses = await account
                .GetAddressesAsync().ConfigureAwait(false);

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
                    Log.Information("Added new addresses {@Addresses}", newAddresses);
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

        private async Task StopTezosBalanceUpdater()
        {
            await _tzktEvents.StopAsync().ConfigureAwait(false);
        }

        #endregion Tezos
    }
}
