using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Helpers;
using Atomex.Common;
using Atomex.Core;
using Atomex.MarketData.Abstract;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.Helpers;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;

namespace Atomex.Swaps
{
    public class SwapManager : ISwapManager
    {
        protected static TimeSpan DefaultCredentialsExchangeTimeout = TimeSpan.FromMinutes(10);
        protected static TimeSpan DefaultMaxSwapTimeout = TimeSpan.FromMinutes(20); // TimeSpan.FromMinutes(40);
        protected static TimeSpan DefaultMaxPaymentTimeout = TimeSpan.FromMinutes(48 * 60);
        protected static TimeSpan SwapTimeoutControlInterval = TimeSpan.FromMinutes(10);

        public event EventHandler<SwapEventArgs> SwapUpdated;

        private readonly IAccount _account;
        private readonly ISwapClient _swapClient;
        private readonly ICurrencyQuotesProvider _quotesProvider;
        private readonly IMarketDataRepository _marketDataRepository;
        private readonly IDictionary<string, ICurrencySwap> _currencySwaps;
        private CancellationTokenSource _cts;
        private Task _workerTask;

        public bool IsRunning => _workerTask != null &&
            !_workerTask.IsCompleted &&
            !_workerTask.IsCanceled &&
            !_workerTask.IsFaulted;

        private static ConcurrentDictionary<long, SemaphoreSlim> _swapsSync;
        private static ConcurrentDictionary<long, SemaphoreSlim> SwapsSync
        {
            get
            {
                var instance = _swapsSync;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _swapsSync, new ConcurrentDictionary<long, SemaphoreSlim>(), null);
                    instance = _swapsSync;
                }

                return instance;
            }
        }

        public SwapManager(
            IAccount account,
            ISwapClient swapClient,
            ICurrencyQuotesProvider quotesProvider,
            IMarketDataRepository marketDataRepository)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _swapClient = swapClient ?? throw new ArgumentNullException(nameof(swapClient));
            _quotesProvider = quotesProvider;
            _marketDataRepository = marketDataRepository ?? throw new ArgumentNullException(nameof(marketDataRepository));

            var currencySwaps = _account.Currencies
                .Select(c =>
                {
                    var currencySwap = CurrencySwapCreator.Create(
                        currency: c,
                        account: _account);

                    currencySwap.InitiatorPaymentConfirmed += InitiatorPaymentConfirmed;
                    currencySwap.AcceptorPaymentConfirmed += AcceptorPaymentConfirmed;
                    currencySwap.AcceptorPaymentSpent += AcceptorPaymentSpent;
                    currencySwap.SwapUpdated += SwapUpdatedHandler;

                    return currencySwap;
                });

            _currencySwaps = currencySwaps.ToDictionary(cs => cs.Currency);
        }

        public void Start()
        {
            if (IsRunning)
                throw new InvalidOperationException("SwapManager already running");

            _cts = new CancellationTokenSource();

            _workerTask = Task.Run(async () =>
            {
                try
                {
                    // restore swaps
                    await RestoreSwapsAsync(_cts.Token)
                        .ConfigureAwait(false);

                    // run swaps timeout control
                    await RunSwapTimeoutControlLoopAsync(_cts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("SwapManager worker task canceled");
                }
                catch (Exception e)
                {
                    Log.Error(e, "SwapManager worker task error");
                }

            }, _cts.Token);

            Log.Information("SwapManager successfully started");
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            _cts.Cancel();

            Clear();

            Log.Information("SwapManager stopped");
        }

        private ICurrencySwap GetCurrencySwap(string currency) => _currencySwaps[currency];

        public async Task<Error> HandleSwapAsync(
            Swap receivedSwap)
        {
            if (!IsRunning)
                throw new InvalidOperationException("SwapManager not started");

            Log.Debug("Handle swap {@swap}", receivedSwap);

            await LockSwapAsync(receivedSwap.Id, _cts.Token)
                .ConfigureAwait(false);

            Log.Debug("Swap {swapId} locked", receivedSwap.Id);

            try
            {
                var swap = await _account
                    .GetSwapByIdAsync(receivedSwap.Id)
                    .ConfigureAwait(false);

                if (receivedSwap.IsInitiator)
                {
                    return await HandleSwapByInitiatorAsync(swap, receivedSwap, _cts.Token)
                        .ConfigureAwait(false);
                }
                else
                {
                    return await HandleSwapByAcceptorAsync(swap, receivedSwap, _cts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                return new Error(Errors.SwapError, $"Swap {receivedSwap.Id} error: {e.Message}");
            }
            finally
            {
                UnlockSwap(receivedSwap.Id);
            }
        }

        private async Task<Error> HandleSwapByInitiatorAsync(
            Swap swap,
            Swap receivedSwap,
            CancellationToken cancellationToken = default)
        {
            if (swap == null && (receivedSwap.Status == SwapStatus.Empty || receivedSwap.Status == SwapStatus.Accepted))
            {
                swap = await AddSwapAsync(receivedSwap, _cts.Token)
                    .ConfigureAwait(false);

                if (receivedSwap.Status == SwapStatus.Accepted)
                {
                    var error = await CheckAndSaveAcceptorRequisitesAsync(swap, receivedSwap, cancellationToken)
                        .ConfigureAwait(false);

                    if (error != null)
                        return error;

                    await UpdateStatusAsync(swap, SwapStatus.Accepted, cancellationToken)
                        .ConfigureAwait(false);
                }

                await FillAndSendInitiatorRequisitesAsync(swap, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (swap != null && swap.Status == SwapStatus.Empty && receivedSwap.Status == SwapStatus.Accepted)
            {
                var error = await CheckAndSaveAcceptorRequisitesAsync(swap, receivedSwap, cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return error;

                await UpdateStatusAsync(swap, SwapStatus.Accepted, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (swap != null && swap.Status == SwapStatus.Empty && receivedSwap.Status == SwapStatus.Initiated)
            {
                await UpdateStatusAsync(swap, SwapStatus.Initiated, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (swap != null && (swap.Status == SwapStatus.Empty || swap.Status == SwapStatus.Initiated || swap.Status == SwapStatus.Accepted) &&
                     receivedSwap.Status == (SwapStatus.Initiated | SwapStatus.Accepted))
            {
                await UpdateStatusAsync(swap, SwapStatus.Initiated | SwapStatus.Accepted, cancellationToken)
                    .ConfigureAwait(false);

                await InitiateSwapAsync(swap, cancellationToken)
                    .ConfigureAwait(false);
            }

            return null; // no error
        }

        private async Task<Error> HandleSwapByAcceptorAsync(
            Swap swap,
            Swap receivedSwap,
            CancellationToken cancellationToken = default)
        {
            if ((swap == null || (swap != null && swap.Status == SwapStatus.Empty)) &&
                (receivedSwap.Status == SwapStatus.Empty || receivedSwap.Status == SwapStatus.Initiated))
            {
                if (swap == null)
                    swap = await AddSwapAsync(receivedSwap, _cts.Token)
                        .ConfigureAwait(false);

                if (receivedSwap.Status == SwapStatus.Initiated)
                {
                    var error = await CheckAndSaveInitiatorRequisitesAsync(swap, receivedSwap, cancellationToken)
                        .ConfigureAwait(false);

                    if (error != null)
                        return error;

                    await UpdateStatusAsync(swap, SwapStatus.Initiated, cancellationToken)
                        .ConfigureAwait(false);

                    await FillAndSendAcceptorRequisitesAsync(swap, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            else if (swap != null && swap.Status == SwapStatus.Initiated && receivedSwap.Status == (SwapStatus.Initiated | SwapStatus.Accepted))
            {
                await UpdateStatusAsync(swap, SwapStatus.Initiated | SwapStatus.Accepted, cancellationToken)
                    .ConfigureAwait(false);

                await GetCurrencySwap(swap.PurchasedCurrency)
                    .StartPartyPaymentControlAsync(swap, cancellationToken)
                    .ConfigureAwait(false);
            }

            return null; // no error
        }

        private async Task UpdateStatusAsync(
            Swap swap,
            SwapStatus status,
            CancellationToken cancellationToken = default)
        {
            swap.Status = status;

            await UpdateSwapAsync(swap, SwapStateFlags.Empty, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<Swap> AddSwapAsync(
            Swap receivedSwap,
            CancellationToken cancellationToken = default)
        {
            var order = await GetOrderAsync(receivedSwap, cancellationToken)
                .ConfigureAwait(false);

            if (order == null || !order.IsApproved) // || !clientSwap.Order.IsContinuationOf(order))
            {
                Log.Warning("Probably swap {@swapId} created on another device", receivedSwap.Id);
                return null;
            }

            var swap = new Swap
            {
                Id                = receivedSwap.Id,
                OrderId           = receivedSwap.OrderId,
                Status            = receivedSwap.Status,
                TimeStamp         = receivedSwap.TimeStamp,
                Symbol            = receivedSwap.Symbol,
                Side              = receivedSwap.Side,
                Price             = receivedSwap.Price,
                Qty               = receivedSwap.Qty,
                IsInitiative      = receivedSwap.IsInitiative,
                MakerNetworkFee   = order.MakerNetworkFee,

                FromAddress       = order.FromAddress,
                FromOutputs       = order.FromOutputs,
                ToAddress         = order.ToAddress,
                RedeemFromAddress = order.RedeemFromAddress,

                // safe counterparty requisites if exists
                PartyAddress         = receivedSwap.PartyAddress,
                PartyRefundAddress   = receivedSwap.PartyRefundAddress,
                PartyRewardForRedeem = receivedSwap.PartyRewardForRedeem,
                PartyRedeemScript    = receivedSwap.PartyRedeemScript
            };

            var result = await _account
                .AddSwapAsync(swap)
                .ConfigureAwait(false);

            if (!result)
            {
                Log.Error("Can't add swap {@swapId} to account swaps repository", receivedSwap.Id);
                return null;
            }

            return swap;
        }

        private Task<Order> GetOrderAsync(
            Swap receivedSwap,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                const int attemptIntervalMs = 100;
                const int maxAttempts = 200;
                var attempts = 0;

                while (attempts < maxAttempts)
                {
                    attempts++;

                    var order = _account.GetOrderById(receivedSwap.OrderId);

                    if (order != null)
                        return order;

                    await Task.Delay(attemptIntervalMs, cancellationToken)
                        .ConfigureAwait(false);
                }

                return null;

            }, cancellationToken);
        }

        private async Task FillAndSendInitiatorRequisitesAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Fill and send initiator requisites for swap {@swapId}", swap.Id);

            var soldCurrency = _account.Currencies.GetByName(swap.SoldCurrency);

            if (swap.Secret == null)
            {
                var secret = _account.Wallet
                    .GetDeterministicSecret(soldCurrency, swap.TimeStamp);

                swap.Secret = secret.SubArray(0, CurrencySwap.DefaultSecretSize);

                await UpdateSwapAsync(swap, SwapStateFlags.HasSecret, cancellationToken)
                    .ConfigureAwait(false);

                secret.Clear();
            }

            if (swap.SecretHash == null)
            {
                swap.SecretHash = CurrencySwap.CreateSwapSecretHash(swap.Secret);

                await UpdateSwapAsync(swap, SwapStateFlags.HasSecretHash, cancellationToken)
                    .ConfigureAwait(false);
            }

            var redeemFromWalletAddress = swap.RedeemFromAddress != null
                ? await _account
                    .GetAddressAsync(swap.PurchasedCurrency, swap.RedeemFromAddress, cancellationToken)
                    .ConfigureAwait(false)
                : null;

            var purchasedCurrency = _account.Currencies.GetByName(swap.PurchasedCurrency);

            swap.RewardForRedeem = await RewardForRedeemHelper
                .EstimateAsync(
                    account: _account,
                    quotesProvider: _quotesProvider,
                    feeCurrencyQuotesProvider: symbol => _marketDataRepository
                        ?.OrderBookBySymbol(symbol)
                        ?.TopOfBook(),
                    redeemableCurrency: purchasedCurrency,
                    redeemFromAddress: redeemFromWalletAddress,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // select refund address for bitcoin based currency
            if (soldCurrency is BitcoinBasedConfig && swap.RefundAddress == null)
            {
                swap.RefundAddress = (await _account
                    .GetCurrencyAccount<BitcoinBasedAccount>(soldCurrency.Name)
                    .GetRefundAddressAsync(cancellationToken)
                    .ConfigureAwait(false))
                    ?.Address;
            }

            await UpdateSwapAsync(swap, SwapStateFlags.Empty, cancellationToken)
                .ConfigureAwait(false);

            _swapClient.SwapInitiateAsync(
                swap.Id,
                swap.SecretHash,
                swap.Symbol,
                swap.ToAddress,
                swap.RewardForRedeem,
                swap.RefundAddress);
        }

        private async Task FillAndSendAcceptorRequisitesAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Fill and send acceptor requisites for swap {@swapId}", swap.Id);

            var redeemFromWalletAddress = swap.RedeemFromAddress != null
                ? await _account
                    .GetAddressAsync(swap.PurchasedCurrency, swap.RedeemFromAddress, cancellationToken)
                    .ConfigureAwait(false)
                : null;

            var purchasedCurrency = _account.Currencies.GetByName(swap.PurchasedCurrency);

            swap.RewardForRedeem = await RewardForRedeemHelper
                .EstimateAsync(
                    account: _account,
                    quotesProvider: _quotesProvider,
                    feeCurrencyQuotesProvider: symbol => _marketDataRepository
                        ?.OrderBookBySymbol(symbol)
                        ?.TopOfBook(),
                    redeemableCurrency: purchasedCurrency,
                    redeemFromAddress: redeemFromWalletAddress,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var soldCurrency = _account.Currencies.GetByName(swap.SoldCurrency);

            // select refund address for bitcoin based currency
            if (soldCurrency is BitcoinBasedConfig && swap.RefundAddress == null)
            {
                swap.RefundAddress = (await _account
                    .GetCurrencyAccount<BitcoinBasedAccount>(soldCurrency.Name)
                    .GetRefundAddressAsync(cancellationToken)
                    .ConfigureAwait(false))
                    ?.Address;
            }

            await UpdateSwapAsync(swap, SwapStateFlags.Empty, cancellationToken)
                .ConfigureAwait(false);

            // send "accept" to other side
            _swapClient.SwapAcceptAsync(
                swap.Id,
                swap.Symbol,
                swap.ToAddress,
                swap.RewardForRedeem,
                swap.RefundAddress);
        }

        private async Task<Error> CheckAndSaveInitiatorRequisitesAsync(
            Swap swap,
            Swap receivedSwap,
            CancellationToken cancellationToken = default)
        {
            if (DateTime.UtcNow > swap.TimeStamp.ToUniversalTime() + DefaultCredentialsExchangeTimeout)
            {
                Log.Error("Handle initiator requisites after swap {@swap} timeout", swap.Id);

                swap.StateFlags |= SwapStateFlags.IsCanceled;

                await UpdateSwapAsync(swap, SwapStateFlags.IsCanceled, cancellationToken)
                    .ConfigureAwait(false);

                return null; // no error
            }

            // check secret hash
            if (swap.SecretHash != null &&
                !swap.SecretHash.SequenceEqual(receivedSwap.SecretHash))
                return new Error(Errors.InvalidSecretHash, $"Secret hash does not match the one already received for swap {swap.Id}");

            if (receivedSwap.SecretHash == null ||
                receivedSwap.SecretHash.Length != CurrencySwap.DefaultSecretHashSize)
                return new Error(Errors.InvalidSecretHash, $"Incorrect secret hash length for swap {swap.Id}");

            Log.Debug("Secret hash {@hash} successfully received", receivedSwap.SecretHash.ToHexString());

            swap.SecretHash = receivedSwap.SecretHash;

            await UpdateSwapAsync(swap, SwapStateFlags.HasSecretHash, cancellationToken)
                .ConfigureAwait(false);

            // check party address
            if (receivedSwap.PartyAddress == null)
                return new Error(Errors.InvalidWallets, $"Incorrect party address for swap {swap.Id}");

            // check party reward for redeem
            if (receivedSwap.RewardForRedeem < 0)
                return new Error(Errors.InvalidRewardForRedeem, $"Incorrect reward for redeem for swap {swap.Id}");

            if (swap.PartyAddress == null)
                swap.PartyAddress = receivedSwap.PartyAddress;

            if (swap.PartyRewardForRedeem == 0 && receivedSwap.PartyRewardForRedeem > 0)
                swap.PartyRewardForRedeem = receivedSwap.PartyRewardForRedeem;

            if (swap.PartyRefundAddress == null)
                swap.PartyRefundAddress = receivedSwap.PartyRefundAddress;

            return null; // no error
        }

        private async Task<Error> CheckAndSaveAcceptorRequisitesAsync(
            Swap swap,
            Swap receivedSwap,
            CancellationToken cancellationToken = default)
        {
            if (DateTime.UtcNow > swap.TimeStamp.ToUniversalTime() + DefaultCredentialsExchangeTimeout)
            {
                Log.Error("Handle acceptpr requisites after swap {@swap} timeout", swap.Id);

                swap.StateFlags |= SwapStateFlags.IsCanceled;

                await UpdateSwapAsync(swap, SwapStateFlags.IsCanceled, cancellationToken)
                    .ConfigureAwait(false);

                return null; // no error
            }

            // check party requisites
            if (receivedSwap.PartyAddress == null)
                return new Error(Errors.InvalidWallets, $"Incorrect party address for swap {swap.Id}");

            if (receivedSwap.RewardForRedeem < 0)
                return new Error(Errors.InvalidRewardForRedeem, $"Incorrect reward for redeem for swap {swap.Id}");

            if (swap.PartyAddress == null)
                swap.PartyAddress = receivedSwap.PartyAddress;

            if (swap.PartyRewardForRedeem == 0 && receivedSwap.PartyRewardForRedeem > 0)
                swap.PartyRewardForRedeem = receivedSwap.PartyRewardForRedeem;

            if (swap.PartyRefundAddress == null)
                swap.PartyRefundAddress = receivedSwap.PartyRefundAddress;

            await UpdateSwapAsync(swap, SwapStateFlags.Empty, cancellationToken)
                .ConfigureAwait(false);

            return null; // no error
        }

        private async Task InitiateSwapAsync(
            Swap swap,
            CancellationToken cancellationToken)
        {
            // broadcast initiator payment
            await GetCurrencySwap(swap.SoldCurrency)
                .PayAsync(swap, cancellationToken)
                .ConfigureAwait(false);

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
            {
                // start redeem control async
                await GetCurrencySwap(swap.SoldCurrency)
                    .StartWaitForRedeemAsync(swap, cancellationToken)
                    .ConfigureAwait(false);
            }

            await GetCurrencySwap(swap.PurchasedCurrency)
                .StartPartyPaymentControlAsync(swap, cancellationToken)
                .ConfigureAwait(false);
        }

        private Task RestoreSwapsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var activeSwapsIds = (await _account
                        .GetSwapsAsync()
                        .ConfigureAwait(false))
                        .Where(s => s.IsActive)
                        .Select(s => s.Id)
                        .ToList();

                    foreach (var activeSwapId in activeSwapsIds)
                    {
                        try
                        {
                            await LockSwapAsync(activeSwapId)
                                .ConfigureAwait(false);

                            var swap = await _account
                                .GetSwapByIdAsync(activeSwapId)
                                .ConfigureAwait(false);

                            await RestoreSwapAsync(swap, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            Log.Error(e, "Swap {@id} restore error", activeSwapId);
                        }
                        finally
                        {
                            UnlockSwap(activeSwapId);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("RestoreSwapsAsync canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, "Swaps restore error");
                }

            }, cancellationToken);
        }

        private async Task RestoreSwapAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentSigned) &&
                !swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
            {
                var txResult = await GetCurrencySwap(swap.SoldCurrency)
                    .TryToFindPaymentAsync(swap, cancellationToken)
                    .ConfigureAwait(false);

                if (txResult == null || txResult.HasError)
                    return; // can't get tx from blockchain

                if (txResult.Value != null)
                {
                    swap.PaymentTx = txResult.Value;
                    swap.PaymentTxId = txResult.Value.Id;
                    swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;

                    await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentBroadcast, cancellationToken)
                        .ConfigureAwait(false);

                    if (txResult.Value.IsConfirmed)
                        swap.StateFlags |= SwapStateFlags.IsPaymentConfirmed;

                    await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentConfirmed, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
            {
                bool waitForRedeem = true;

                if (!swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentConfirmed) &&
                    DateTime.UtcNow > swap.TimeStamp.ToUniversalTime() + DefaultMaxPaymentTimeout)
                {
                    var currency = _account.Currencies
                        .GetByName(swap.PaymentTx.Currency);

                    var result = await currency
                        .IsTransactionConfirmed(
                            txId: swap.PaymentTx.Id,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (result.HasError || !result.Value.IsConfirmed)
                    {
                        waitForRedeem = false;

                        Log.Debug("Swap {@id} canceled in RestoreSwapAsync. Timeout reached.", swap.Id);

                        swap.StateFlags |= SwapStateFlags.IsCanceled;

                        await UpdateSwapAsync(swap, SwapStateFlags.IsCanceled, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                if (waitForRedeem)
                {
                    await GetCurrencySwap(swap.SoldCurrency)
                        .StartWaitForRedeemAsync(swap, cancellationToken)
                        .ConfigureAwait(false);

                    if (swap.IsInitiator)
                    {
                        // check acceptor payment confirmation
                        await GetCurrencySwap(swap.PurchasedCurrency)
                            .StartPartyPaymentControlAsync(swap, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (swap.IsAcceptor && swap.RewardForRedeem > 0)
                    {
                        await GetCurrencySwap(swap.PurchasedCurrency)
                            .StartWaitForRedeemBySomeoneAsync(swap, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            else
            {
                if (IsPaymentDeadlineReached(swap))
                {
                    await CancelSwapByTimeoutAsync(swap, cancellationToken)
                        .ConfigureAwait(false);

                    return;
                }

                if (swap.IsAcceptor)
                {
                    if (!swap.Status.HasFlag(SwapStatus.Initiated)) // not initiated
                    {
                        // try to get actual swap status from server and accept swap
                        _swapClient.SwapStatusAsync($"get_swap_{swap.Id}", swap.Id);
                    }
                    else if (swap.Status.HasFlag(SwapStatus.Initiated) && // initiated but not accepted
                            !swap.Status.HasFlag(SwapStatus.Accepted))
                    {
                        // try to get actual swap status from server and accept swap
                        _swapClient.SwapStatusAsync($"get_swap_{swap.Id}", swap.Id);
                    }
                    else if (swap.Status.HasFlag(SwapStatus.Initiated) && // initiated and accepted
                             swap.Status.HasFlag(SwapStatus.Accepted))
                    {
                        // wait for initiator tx
                        await GetCurrencySwap(swap.PurchasedCurrency)
                            .StartPartyPaymentControlAsync(swap)
                            .ConfigureAwait(false);
                    }
                }
                else if (swap.IsInitiator)
                {
                    if (!swap.Status.HasFlag(SwapStatus.Initiated)) // not initiated
                    {
                        // send initiator's requisites
                        await FillAndSendInitiatorRequisitesAsync(swap)
                            .ConfigureAwait(false);
                    }
                    else if (swap.Status.HasFlag(SwapStatus.Initiated) && // initiated but not accepted
                            !swap.Status.HasFlag(SwapStatus.Accepted))
                    {
                        // try to get actual swap status from server
                        _swapClient.SwapStatusAsync($"get_swap_{swap.Id}", swap.Id);
                    }
                    else if (swap.Status.HasFlag(SwapStatus.Initiated) && // initiated and accepted
                             swap.Status.HasFlag(SwapStatus.Accepted))
                    {
                        // broadcast initiator payment again
                        await InitiateSwapAsync(swap, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        private Task RunSwapTimeoutControlLoopAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(SwapTimeoutControlInterval, cancellationToken)
                            .ConfigureAwait(false);

                        var swaps = (await _account
                            .GetSwapsAsync()
                            .ConfigureAwait(false))
                            .Where(s => s.IsActive && !s.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
                            .ToList();

                        foreach (var swap in swaps)
                        {
                            try
                            {
                                if (IsPaymentDeadlineReached(swap))
                                    await CancelSwapByTimeoutAsync(swap, cancellationToken)
                                        .ConfigureAwait(false);
                            }
                            catch (Exception e)
                            {
                                Log.Error(e, "Swap {@id} restore error", swap.Id);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("Swaps timeout control canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, "Swaps timeout control error.");
                }

            }, cancellationToken);
        }

        private bool IsPaymentDeadlineReached(Swap swap)
        {
            var paymentDeadline = swap.IsInitiator
                ? swap.TimeStamp.ToUniversalTime() + DefaultMaxSwapTimeout
                : swap.TimeStamp.ToUniversalTime().AddSeconds(CurrencySwap.DefaultAcceptorLockTimeInSeconds) - CurrencySwap.PaymentTimeReserve;

            return DateTime.UtcNow > paymentDeadline;
        }

        private async Task CancelSwapByTimeoutAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Swap {@id} canceled. Timeout reached.", swap.Id);

            swap.StateFlags |= SwapStateFlags.IsCanceled;

            await UpdateSwapAsync(swap, SwapStateFlags.IsCanceled, cancellationToken)
                .ConfigureAwait(false);
        }

        private Task UpdateSwapAsync(
            Swap swap,
            SwapStateFlags changedFlag,
            CancellationToken cancellationToken = default) =>
            SwapUpdatedHandler(this, new SwapEventArgs(swap, changedFlag), cancellationToken);

        private async Task SwapUpdatedHandler(
            object sender,
            SwapEventArgs args,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Update swap {@swap} in db", args.Swap.Id);

                var result = await _account
                    .UpdateSwapAsync(args.Swap)
                    .ConfigureAwait(false);

                if (!result)
                    Log.Error("Swap update error");

                SwapUpdated?.Invoke(this, args);
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap update error");
            }
        }

        private async Task InitiatorPaymentConfirmed(
            ICurrencySwap currencySwap,
            SwapEventArgs swapArgs,
            CancellationToken cancellationToken = default)
        {
            var swap = swapArgs.Swap;

            Log.Debug("Initiator payment confirmed event for swap {@swapId}", swap.Id);

            try
            {
                // broadcast acceptors payment tx (using sold currency protocol)
                if (swap.IsAcceptor &&
                    swap.IsPurchasedCurrency(currencySwap.Currency))
                {
                    await GetCurrencySwap(swap.SoldCurrency)
                        .PayAsync(swap, cancellationToken)
                        .ConfigureAwait(false);

                    if (swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
                    {
                        // start redeem control async
                        await GetCurrencySwap(swap.SoldCurrency)
                            .StartWaitForRedeemAsync(swap, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    // wait for redeem by other party or someone else
                    if (swap.RewardForRedeem > 0)
                        await GetCurrencySwap(swap.PurchasedCurrency)
                            .StartWaitForRedeemBySomeoneAsync(swap, cancellationToken)
                            .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Initiator payment confirmed handler error");
            }
        }

        private async Task AcceptorPaymentConfirmed(
            ICurrencySwap currencySwap,
            SwapEventArgs swapArgs,
            CancellationToken cancellationToken = default)
        {
            var swap = swapArgs.Swap;

            Log.Debug("Acceptor payment confirmed event for swap {@swapId}", swap.Id);

            try
            {
                // party redeem
                if (swap.IsInitiator &&
                    swap.IsPurchasedCurrency(currencySwap.Currency) &&
                    swap.PartyRewardForRedeem > 0) // todo: user param >= 2*RedeemFee
                {
                    await GetCurrencySwap(swap.SoldCurrency)
                        .RedeemForPartyAsync(swap, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Acceptor payment confirmed handler error");
            }
        }

        private async Task AcceptorPaymentSpent(
            ICurrencySwap currencySwap,
            SwapEventArgs swapArgs,
            CancellationToken cancellationToken = default)
        {
            var swap = swapArgs.Swap;

            Log.Debug("Acceptor payment spent event for swap {@swapId}", swap.Id);

            try
            {
                // redeem by acceptor async (using purchased currency protocol)
                if (swap.IsAcceptor &&
                    swap.IsSoldCurrency(currencySwap.Currency) &&
                    swap.RewardForRedeem == 0)
                {
                    await GetCurrencySwap(swap.PurchasedCurrency)
                        .RedeemAsync(swap, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Acceptor payment spent handler error");
            }
        }

        private async Task LockSwapAsync(
            long swapId,
            CancellationToken cancellationToken = default)
        {
            if (SwapsSync.TryGetValue(swapId, out SemaphoreSlim semaphore))
            {
                await semaphore
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                semaphore = new SemaphoreSlim(1, 1);

                await semaphore
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (!SwapsSync.TryAdd(swapId, semaphore))
                {
                    semaphore.Release();
                    semaphore.Dispose();

                    if (SwapsSync.TryGetValue(swapId, out semaphore))
                    {
                        await semaphore
                            .WaitAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        Log.Error("There is no semaphore =(");

                        throw new Exception("Swap lock error");
                    }
                }
            }
        }

        private void UnlockSwap(long id)
        {
            if (SwapsSync.TryGetValue(id, out var semaphore))
            {
                try
                {
                    semaphore.Release();
                }
                catch (SemaphoreFullException)
                {
                    Log.Warning($"Semaphore for swap {id} is already released");
                }
                catch (ObjectDisposedException)
                {
                    Log.Warning($"Semaphore for swap {id} is already disposed");
                }
            }
        }

        private void Clear()
        {
            foreach (var swapSync in SwapsSync)
            {
                var swapId = swapSync.Key;
                var semaphore = swapSync.Value;

                if (semaphore.CurrentCount == 0)
                {
                    try
                    {
                        semaphore.Release();
                    }
                    catch (SemaphoreFullException)
                    {
                        Log.Warning($"Semaphore for swap {swapId} is already released");
                    }
                    catch (ObjectDisposedException)
                    {
                        Log.Warning($"Semaphore for swap {swapId} is already disposed");
                    }

                    try
                    {
                        semaphore.Dispose();
                    }
                    catch (Exception)
                    {
                        Log.Warning($"Semaphore for swap {swapId} is already disposed");
                    }
                }
            }

            SwapsSync.Clear();
        }
    }
}