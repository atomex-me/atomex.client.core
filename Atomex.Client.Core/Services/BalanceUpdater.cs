using System;
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
                }
                catch (OperationCanceledException)
                {
                    _log.Debug("BalanceUpdater canceled");
                }
                catch (Exception e)
                {
                    _log.Error(e, "BalanceUpdater error on starting");
                }

            }, _cts.Token);

            _log.Information("BalanceUpdater successfully started");
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            Task.Run(async () =>
            {
                await StopTezosBalanceUpdater().ConfigureAwait(false);
            });

            _cts.Cancel();

            _log.Information("BalanceUpdater stopped");
            _isRunning = false;
        }


        #region Tezos

        private async Task StartTezosBalanceUpdater()
        {
            if (_currenciesProvider == null)
            {
                throw new InvalidOperationException("StartTezosBalanceUpdater was called before CurrenciesProvider initialization");
            }

            var currency = _currenciesProvider
                .GetCurrencies(_account.Network)
                .Get<TezosConfig>(TezosConfig.Xtz);
            var baseUri = currency.BaseUri;

            await _tzktEvents.StartAsync(baseUri).ConfigureAwait(false);
            
            var account = _account.GetCurrencyAccount(currency.Name);
            var addresses = (await account
                    .GetAddressesAsync())
                .Select(wa => wa.Address);
            

            await _tzktEvents
                .NotifyOnAccountsAsync(addresses, TezosBalanceUpdateHandler)
                .ConfigureAwait(false);
        }

        private async void TezosBalanceUpdateHandler(string address)
        {
            try
            {
                await _walletScanner
                    .ScanAddressAsync(TezosConfig.Xtz, address)
                    .ConfigureAwait(false);
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
