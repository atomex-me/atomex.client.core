using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Abstract;
using Atomex.Blockchain.SoChain;
using Atomex.Services.Abstract;
using Atomex.Services.BalanceUpdaters;
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
        private readonly IHdWalletScanner _walletScanner;

        private CancellationTokenSource _cts;
        private bool _isRunning;
        
        private readonly IList<IChainBalanceUpdater> _balanceUpdaters = new List<IChainBalanceUpdater>();
        private const string SoChainRealtimeApiHost = "pusher.chain.so";

        public BalanceUpdater(IAccount account, ICurrenciesProvider currenciesProvider, ILogger log)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _currenciesProvider = currenciesProvider;
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _walletScanner = new HdWalletScanner(_account);

            InitChainBalanceUpdaters();
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
                    var startTasks = _balanceUpdaters
                        .Select(x => x.StartAsync());
                    await Task.WhenAll(startTasks).ConfigureAwait(false);

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
                    var stopTasks = _balanceUpdaters
                        .Select(x => x.StopAsync());
                    await Task.WhenAll(stopTasks).ConfigureAwait(false);
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

        private void InitChainBalanceUpdaters()
        {
            try
            {
                var soChainRealtimeApi = new SoChainRealtimeApi(SoChainRealtimeApiHost, _log);
                _balanceUpdaters.Add(new BitcoinBalanceUpdater(_account, _walletScanner, soChainRealtimeApi, _log));
                _balanceUpdaters.Add(new LitecoinBalanceUpdater(_account, _walletScanner, soChainRealtimeApi, _log));

                _balanceUpdaters.Add(new TezosBalanceUpdater(_account, _currenciesProvider, _walletScanner, _log));
            }
            catch (Exception e)
            {
                _log.Error(e, "Error while initializing chain balance updaters");
            }
        }
    }
}
