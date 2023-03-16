using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using Atomex.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Services.Abstract;
using Atomex.TzktEvents;
using Atomex.TzktEvents.Models;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Tezos;

namespace Atomex.Services.BalanceUpdaters
{
    public class TezosTokenBalanceUpdater : IChainBalanceUpdater
    {
        private readonly IAccount _account;
        private readonly TezosAccount _tezosAccount;
        private readonly ICurrenciesProvider _currenciesProvider;
        private readonly ILogger? _log;
        private readonly ITzktEventsClient _tzkt;
        private readonly TezosTokensWalletScanner[] _walletScanners;

        private ISet<string> _addresses;

        public TezosTokenBalanceUpdater(
            IAccount account,
            ICurrenciesProvider currenciesProvider,
            TezosTokensWalletScanner[] walletScanners,
            ITzktEventsClient tzkt,
            ILogger? log = null)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _tezosAccount = account.GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz);
            _currenciesProvider = currenciesProvider;
            _walletScanners = walletScanners ?? throw new ArgumentNullException(nameof(walletScanners));
            _tzkt = tzkt ?? throw new ArgumentNullException(nameof(tzkt));
            _log = log;
        }

        public async Task StartAsync()
        {
            if (_currenciesProvider == null)
            {
                throw new InvalidOperationException("Start TezosTokenBalanceUpdater was called before CurrenciesProvider initialization");
            }

            try
            {
                var xtzConfig = _currenciesProvider
                    .GetCurrencies(_account.Network)
                    .Get<TezosConfig>(TezosConfig.Xtz);

                var baseUri = xtzConfig.BaseUri;

                await _tzkt
                    .StartAsync(baseUri)
                    .ConfigureAwait(false);

                _addresses = await GetAddressesAsync()
                    .ConfigureAwait(false);

                await _tzkt
                    .NotifyOnTokenBalancesAsync(_addresses, BalanceUpdatedHandler)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log?.LogError(e, "Error on starting TezosTokenBalanceUpdater");
            }
        }

        public async Task StopAsync()
        {
            try
            {
                await _tzkt
                    .StopAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log?.LogError(e, "Error on stopping TezosTokenBalanceUpdater");
            }
        }

        private async Task<ISet<string>> GetAddressesAsync()
        {
            // all tezos addresses
            var xtzAddresses = await _tezosAccount.LocalStorage
                .GetAddressesAsync(TezosConfig.Xtz)
                .ConfigureAwait(false);

            if (xtzAddresses.Count() <= 1)
            {
                // firstly scan xtz
                await new TezosWalletScanner(_tezosAccount)
                    .ScanAsync()
                    .ConfigureAwait(false);

                xtzAddresses = await _tezosAccount
                    .LocalStorage
                    .GetAddressesAsync(TezosConfig.Xtz)
                    .ConfigureAwait(false);
            }

            var walletAddresses = xtzAddresses.ToList();

            var freeAddress = await _tezosAccount
                .GetFreeExternalAddressAsync()
                .ConfigureAwait(false);

            walletAddresses.Add(freeAddress);

            // addresses from local db
            var fa12Addresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(currency: TezosHelper.Fa12)
                .ConfigureAwait(false);

            var fa2Addresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(currency: TezosHelper.Fa2)
                .ConfigureAwait(false);

            var addresses = fa12Addresses
                .Concat(fa2Addresses)
                .Select(a => a.Address)
                .ToHashSet();

            if (walletAddresses.Any())
                addresses.UnionWith(walletAddresses.Select(a => a.Address));

            return addresses;
        }

        private async void BalanceUpdatedHandler(TezosTokenEvent @event)
        {
            try
            {
                if (@event.Contract != null)
                {
                    var walletScanner = _walletScanners.FirstOrDefault(s => s.TokenType == @event.Standard);

                    if (walletScanner == null)
                        throw new Exception($"Can't find standard {@event.Standard}");

                    await walletScanner
                        .UpdateBalanceAsync(@event.Address, @event.Contract, (int)@event.TokenId)
                        .ConfigureAwait(false);
                }
                else
                {
                    foreach (var walletScanner in _walletScanners)
                    {
                        await walletScanner
                            .UpdateBalanceAsync(@event.Address)
                            .ConfigureAwait(false);
                    }
                }

                var newAddresses = await GetAddressesAsync()
                    .ConfigureAwait(false);

                newAddresses.ExceptWith(_addresses);

                if (newAddresses.Any())
                {
                    _log?.LogInformation("TezosTokenBalanceUpdater adds new addresses {@Addresses}", newAddresses);

                    await _tzkt
                        .NotifyOnTokenBalancesAsync(newAddresses, BalanceUpdatedHandler)
                        .ConfigureAwait(false);

                    _addresses.UnionWith(newAddresses);
                }
            }
            catch (Exception e)
            {
                _log?.LogError(e, "Error on handling Tezos balance update");
            }
        }
    }
}