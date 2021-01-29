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
using Atomex.Swaps.Abstract;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;

namespace Atomex.Swaps
{
    public class SwapManager : ISwapManager
    {
        protected static TimeSpan DefaultCredentialsExchangeTimeout = TimeSpan.FromMinutes(10);
        protected static TimeSpan DefaultMaxSwapTimeout = TimeSpan.FromMinutes(20); // TimeSpan.FromMinutes(40);
        protected static TimeSpan DefaultMaxPaymentTimeout = TimeSpan.FromMinutes(48*60);
        protected static TimeSpan SwapTimeoutControlInterval = TimeSpan.FromMinutes(10);

        public event EventHandler<SwapEventArgs> SwapUpdated;

        private readonly IAccount _account;
        private readonly ISwapClient _swapClient;
        private readonly IDictionary<string, ICurrencySwap> _currencySwaps;

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


        public SwapManager(IAccount account, ISwapClient swapClient)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _swapClient = swapClient ?? throw new ArgumentNullException(nameof(swapClient));

            _currencySwaps = _account.Currencies
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
                })
                .ToDictionary(cs => cs.Currency);
        }

        public void Clear()
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

        private ICurrencySwap GetCurrencySwap(string currency) => _currencySwaps[currency];

        public async Task<Error> HandleSwapAsync(
            Swap receivedSwap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle swap {@swap}", receivedSwap.ToString());

            await LockSwapAsync(receivedSwap.Id, cancellationToken)
                .ConfigureAwait(false);

            Log.Debug("Swap {@swap} locked", receivedSwap.Id);

            try
            {
                var swap = await _account
                    .GetSwapByIdAsync(receivedSwap.Id)
                    .ConfigureAwait(false);

                if (swap == null)
                {
                    swap = await AddSwapAsync(receivedSwap)
                        .ConfigureAwait(false);

                    if (swap != null && swap.IsInitiator)
                        await InitiateSwapAsync(swap, cancellationToken)
                            .ConfigureAwait(false);
                }
                else
                {
                    var error = await HandleExistingSwapAsync(swap, receivedSwap, cancellationToken)
                        .ConfigureAwait(false);

                    if (error != null)
                        return error;
                }

                return null;
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

        private async Task<Swap> AddSwapAsync(Swap receivedSwap)
        {
            var order = await GetOrderAsync(receivedSwap)
                .ConfigureAwait(false);

            if (order == null || !order.IsApproved) // || !clientSwap.Order.IsContinuationOf(order))
            {
                Log.Warning("Probably swap {@swapId} created on another device", receivedSwap.Id);
                return null;
            }

            var swap = new Swap
            {
                Id              = receivedSwap.Id,
                OrderId         = receivedSwap.OrderId,
                Status          = receivedSwap.Status,
                TimeStamp       = receivedSwap.TimeStamp,
                Symbol          = receivedSwap.Symbol,
                Side            = receivedSwap.Side,
                Price           = receivedSwap.Price,
                Qty             = receivedSwap.Qty,
                IsInitiative    = receivedSwap.IsInitiative,
                MakerNetworkFee = order.MakerNetworkFee
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

        private Task<Order> GetOrderAsync(Swap receivedSwap)
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

                    await Task.Delay(attemptIntervalMs)
                        .ConfigureAwait(false);
                }

                return null;
            });
        }

        private async Task InitiateSwapAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Initiate swap {@swapId}", swap.Id);

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

            WalletAddress toAddress;

            // select self address for purchased currency
            if (swap.ToAddress == null)
            {
                toAddress = await _account
                    .GetRedeemAddressAsync(swap.PurchasedCurrency, cancellationToken)
                    .ConfigureAwait(false);

                swap.ToAddress = toAddress.Address;

                await UpdateSwapAsync(swap, SwapStateFlags.Empty, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                toAddress = await _account
                    .GetAddressAsync(swap.PurchasedCurrency, swap.ToAddress, cancellationToken)
                    .ConfigureAwait(false);
            }

            swap.RewardForRedeem = await GetRewardForRedeemAsync(toAddress, cancellationToken)
                .ConfigureAwait(false);

            // select refund address for bitcoin based currency
            if (soldCurrency is BitcoinBasedCurrency && swap.RefundAddress == null)
            {
                swap.RefundAddress = (await _account
                    .GetCurrencyAccount<BitcoinBasedAccount>(soldCurrency.Name)
                    .GetRefundAddressAsync(cancellationToken)
                    .ConfigureAwait(false))
                    ?.Address;
            }

            await UpdateSwapAsync(swap, SwapStateFlags.Empty, cancellationToken)
                .ConfigureAwait(false);

            _swapClient.SwapInitiateAsync(swap);
        }

        private async Task<Error> HandleExistingSwapAsync(
            Swap swap,
            Swap receivedSwap,
            CancellationToken cancellationToken = default)
        {
            Error error = null;

            try
            {
                if (swap.IsAcceptor && IsInitiate(swap, receivedSwap))
                {
                    // handle initiate by acceptor
                    error = await HandleInitiateAsync(swap, receivedSwap, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (swap.IsInitiator && IsAccept(swap, receivedSwap))
                {
                    // handle accept by initiator
                    error = await HandleAcceptAsync(swap, receivedSwap, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Existing swap handle error");
            }

            // update swap status
            swap.Status = receivedSwap.Status;

            await UpdateSwapAsync(swap, SwapStateFlags.Empty, cancellationToken)
                .ConfigureAwait(false);

            return error;
        }

        private bool IsInitiate(Swap swap, Swap receivedSwap) =>
            swap.IsStatusSet(receivedSwap.Status, SwapStatus.Initiated);

        private bool IsAccept(Swap swap, Swap receivedSwap) =>
            swap.IsStatusSet(receivedSwap.Status, SwapStatus.Accepted);

        private async Task<Error> HandleInitiateAsync(
            Swap swap,
            Swap receivedSwap,
            CancellationToken cancellationToken = default)
        {
            if (DateTime.UtcNow > swap.TimeStamp.ToUniversalTime() + DefaultCredentialsExchangeTimeout)
            {
                Log.Error("Handle initiate after swap {@swap} timeout", swap.Id);

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

            // create self requisites
            if (swap.ToAddress == null)
            {
                var walletAddress = await _account
                    .GetRedeemAddressAsync(swap.PurchasedCurrency, cancellationToken)
                    .ConfigureAwait(false);

                swap.ToAddress = walletAddress.Address;
                swap.RewardForRedeem = await GetRewardForRedeemAsync(walletAddress, cancellationToken)
                    .ConfigureAwait(false);
            }

            var soldCurrency = _account.Currencies.GetByName(swap.SoldCurrency);

            // select refund address for bitcoin based currency
            if (soldCurrency is BitcoinBasedCurrency && swap.RefundAddress == null)
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
            _swapClient.SwapAcceptAsync(swap);

            await GetCurrencySwap(swap.PurchasedCurrency)
                .StartPartyPaymentControlAsync(swap, cancellationToken)
                .ConfigureAwait(false);

            return null; // no error
        }

        private async Task<Error> HandleAcceptAsync(
            Swap swap,
            Swap receivedSwap,
            CancellationToken cancellationToken = default)
        {
            if (DateTime.UtcNow > swap.TimeStamp.ToUniversalTime() + DefaultCredentialsExchangeTimeout)
            {
                Log.Error("Handle accept after swap {@swap} timeout", swap.Id);

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

            return null; // no error
        }

        public async Task RestoreSwapsAsync(
            CancellationToken cancellationToken = default)
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
            catch (Exception e)
            {
                Log.Error(e, "Swaps restore error");
            }

            SwapTimeoutControlAsync(cancellationToken)
                .FireAndForget();
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
                    swap.PaymentTx  = txResult.Value;
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
                    var result = await swap.PaymentTx
                        .IsTransactionConfirmed(
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
                        _swapClient.SwapStatusAsync(new Request<Swap>() { Id = $"get_swap_{swap.Id}", Data = swap });
                    }
                    else if (swap.Status.HasFlag(SwapStatus.Initiated) && // initiated but not accepted
                            !swap.Status.HasFlag(SwapStatus.Accepted))
                    {
                        // try to get actual swap status from server and accept swap
                        _swapClient.SwapStatusAsync(new Request<Swap>() { Id = $"get_swap_{swap.Id}", Data = swap });
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
                        // initiate
                        await InitiateSwapAsync(swap)
                            .ConfigureAwait(false);
                    }
                    else if (swap.Status.HasFlag(SwapStatus.Initiated) && // initiated but not accepted
                            !swap.Status.HasFlag(SwapStatus.Accepted))
                    {
                        // try to get actual swap status from server
                        _swapClient.SwapStatusAsync(new Request<Swap>() { Id = $"get_swap_{swap.Id}", Data = swap });
                    }
                    else if (swap.Status.HasFlag(SwapStatus.Initiated) && // initiated and accepted
                             swap.Status.HasFlag(SwapStatus.Accepted))
                    {
                        // broadcast initiator payment again
                        await GetCurrencySwap(swap.SoldCurrency)
                            .PayAsync(swap)
                            .ConfigureAwait(false);

                        if (swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
                        {
                            // start redeem control async
                            await GetCurrencySwap(swap.SoldCurrency)
                                .StartWaitForRedeemAsync(swap)
                                .ConfigureAwait(false);
                        }

                        await GetCurrencySwap(swap.PurchasedCurrency)
                            .StartPartyPaymentControlAsync(swap)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        private Task SwapTimeoutControlAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(SwapTimeoutControlInterval)
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
                catch (Exception e)
                {
                    Log.Error(e, "Swaps timeout control error");
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
                    .WaitAsync()
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

        private async Task<decimal> GetRewardForRedeemAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default)
        {
            var currency = _account
                .Currencies
                .GetByName(walletAddress.Currency);

            var feeCurrency = currency.FeeCurrencyName;

            var feeCurrencyAddress = await _account
                .GetAddressAsync(feeCurrency, walletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            if (feeCurrencyAddress == null)
            {
                feeCurrencyAddress = await _account
                    .DivideAddressAsync(
                        currency: feeCurrency,
                        chain: walletAddress.KeyIndex.Chain,
                        index: walletAddress.KeyIndex.Index)
                    .ConfigureAwait(false);

                if (feeCurrencyAddress == null)
                    throw new Exception($"Can't get/devide {currency.Name} address {walletAddress.Address} for {feeCurrency}");
            }

            var redeemFee = await currency
                .GetRedeemFeeAsync(walletAddress, cancellationToken)
                .ConfigureAwait(false);

            return feeCurrencyAddress.AvailableBalance() < redeemFee
                ? await currency.GetRewardForRedeemAsync(cancellationToken)
                    .ConfigureAwait(false)
                : 0;
        }
    }
}