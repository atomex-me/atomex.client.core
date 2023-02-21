using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using Atomex.Abstract;
using Atomex.Blockchain.SoChain;
using Atomex.Services.Abstract;
using Atomex.Services.BalanceUpdaters;
using Atomex.TzktEvents;
using Atomex.Wallet;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Tezos;

namespace Atomex.Services
{
    public class BalanceUpdater : IBalanceUpdater
    {
        private readonly IAccount _account;
        private readonly ICurrenciesProvider _currenciesProvider;
        private readonly ILogger? _log;
        private readonly IWalletScanner _walletScanner;

        private CancellationTokenSource _cts;
        private bool _isRunning;
        
        private readonly IList<IChainBalanceUpdater> _balanceUpdaters = new List<IChainBalanceUpdater>();
        private const string SoChainRealtimeApiHost = "pusher.chain.so";

        public BalanceUpdater(IAccount account, ICurrenciesProvider currenciesProvider, ILogger? log = null)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _currenciesProvider = currenciesProvider;
            _log = log;
            _walletScanner = new WalletScanner(_account);

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

                    await Task.WhenAll(startTasks)
                        .ConfigureAwait(false);

                    _log?.LogInformation("BalanceUpdater successfully started");
                }
                catch (OperationCanceledException)
                {
                    _log?.LogDebug("BalanceUpdater canceled");
                }
                catch (Exception e)
                {
                    _log?.LogError(e, "Unconfirmed BalanceUpdater error");
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

                    await Task.WhenAll(stopTasks)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _log?.LogError(e, "Error while stopping BalanceUpdater");
                }
            });

            _cts.Cancel();
            _isRunning = false;
            _log?.LogInformation("BalanceUpdater stopped");
        }

        private void InitChainBalanceUpdaters()
        {
            try
            {
                var soChainRealtimeApi = new SoChainRealtimeApi(SoChainRealtimeApiHost, _log);
                _balanceUpdaters.Add(new BitcoinBalanceUpdater(_account, _walletScanner, soChainRealtimeApi, _log));
                _balanceUpdaters.Add(new LitecoinBalanceUpdater(_account, _walletScanner, soChainRealtimeApi, _log));

                _balanceUpdaters.Add(new EthereumBalanceUpdater(_account, _currenciesProvider, _walletScanner, _log));
                _balanceUpdaters.Add(new Erc20BalanceUpdater(_account, _currenciesProvider, _walletScanner, _log));

                var tzkt = new TzktEventsClient(_log);
                _balanceUpdaters.Add(new TezosBalanceUpdater(_account, _currenciesProvider, _walletScanner, tzkt, _log));
                _balanceUpdaters.Add(new TezosTokenBalanceUpdater(_account, _currenciesProvider, new TezosTokensWalletScanner(_account.GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz)), tzkt, _log));
            }
            catch (Exception e)
            {
                _log?.LogError(e, "Error while initializing chain balance updaters");
            }
        }
    }
}