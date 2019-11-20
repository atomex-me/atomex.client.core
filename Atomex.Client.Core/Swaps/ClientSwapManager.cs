using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Atomex.Swaps.Abstract;
using Atomex.Wallet.Abstract;
using Serilog;

namespace Atomex.Swaps
{
    public class ClientSwapManager : IClientSwapManager
    {
        protected static TimeSpan DefaultMaxSwapTimeout = TimeSpan.FromMinutes(20);

        public event EventHandler<SwapEventArgs> SwapUpdated;

        private readonly IAccount _account;
        private readonly ISwapClient _swapClient;
        private readonly IDictionary<string, ICurrencySwap> _currencySwaps;
        private readonly ConcurrentDictionary<long, SemaphoreSlim> _semaphores;

        public ClientSwapManager(IAccount account, ISwapClient swapClient)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _swapClient = swapClient ?? throw new ArgumentNullException(nameof(swapClient));

            _currencySwaps = _account.Currencies
                .Select(c =>
                {
                    var currencySwap = CurrencySwapCreator.Create(
                        currency: c,
                        account: _account,
                        swapClient: swapClient);

                    currencySwap.InitiatorPaymentConfirmed += InitiatorPaymentConfirmed;
                    currencySwap.AcceptorPaymentConfirmed += AcceptorPaymentConfirmed;
                    currencySwap.AcceptorPaymentSpent += AcceptorPaymentSpent;
                    currencySwap.SwapUpdated += SwapUpdatedHandler;

                    return currencySwap;
                })
                .ToDictionary(cs => cs.Currency.Name);

            _semaphores = new ConcurrentDictionary<long, SemaphoreSlim>();
        }

        private ICurrencySwap GetCurrencySwap(Currency currency) => _currencySwaps[currency.Name];

        public async Task HandleSwapAsync(ClientSwap receivedSwap)
        {
            Log.Debug("Handle swap {@swap}", receivedSwap.ToString());

            await LockSwapAsync(receivedSwap.Id)
                .ConfigureAwait(false);

            try
            {
                var swap = await _account
                    .GetSwapByIdAsync(receivedSwap.Id)
                    .ConfigureAwait(false);

                if (swap == null)
                {
                    await RunSwapAsync(receivedSwap)
                        .ConfigureAwait(false);
                }
                else
                {
                    await HandleExistingSwapAsync(swap, receivedSwap)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                UnlockSwap(receivedSwap.Id);

                throw;
            }

            UnlockSwap(receivedSwap.Id);
        }

        private async Task RunSwapAsync(ClientSwap receivedSwap)
        {
            //var order = _account.GetOrderById(clientSwap.Order.ClientOrderId);

            //if (order == null || !clientSwap.Order.IsContinuationOf(order))
            //{
            //    Log.Warning("Probably swap {@swapId} created on another device", clientSwap.Id);
            //    return;
            //}

            var swap = new ClientSwap
            {
                Id = receivedSwap.Id,
                Status = receivedSwap.Status,
                TimeStamp = receivedSwap.TimeStamp,
                Symbol = receivedSwap.Symbol,
                Side = receivedSwap.Side,
                Price = receivedSwap.Price,
                Qty = receivedSwap.Qty,
                IsInitiative = receivedSwap.IsInitiative,
            };

            var result = await _account
                .AddSwapAsync(swap)
                .ConfigureAwait(false);

            if (!result)
            {
                Log.Error("Can't add swap {@swapId} to account swaps repository", receivedSwap.Id);
                return;
            }

            if (swap.IsInitiator)
            {
                await InitiateSwapAsync(swap)
                    .ConfigureAwait(false);
            }
        }

        private async Task InitiateSwapAsync(ClientSwap swap)
        {
            Log.Debug("Initiate swap {@swapId}", swap.Id);

            if (swap.Secret == null)
            {
                var secret = _account.Wallet
                    .GetDeterministicSecret(swap.SoldCurrency, swap.TimeStamp);

                swap.Secret = secret.SubArray(0, CurrencySwap.DefaultSecretSize);
                RaiseSwapUpdated(swap, SwapStateFlags.HasSecret);

                secret.Clear();
            }

            if (swap.SecretHash == null)
            {
                swap.SecretHash = CurrencySwap.CreateSwapSecretHash(swap.Secret);
                RaiseSwapUpdated(swap, SwapStateFlags.HasSecretHash);
            }

            if (swap.ToAddress == null)
            {
                swap.ToAddress = (await _account
                    .GetRedeemAddressAsync(swap.PurchasedCurrency)
                    .ConfigureAwait(false))
                    .Address;
                RaiseSwapUpdated(swap, SwapStateFlags.Empty);
            }

            swap.RewardForRedeem = await RewardForRedeemAsync(swap)
                .ConfigureAwait(false);

            RaiseSwapUpdated(swap, SwapStateFlags.Empty);

            _swapClient.SwapInitiateAsync(swap);
        }

        private async Task HandleExistingSwapAsync(ClientSwap swap, ClientSwap receivedSwap)
        {
            try
            {
                if (IsInitiate(swap, receivedSwap) && swap.IsAcceptor)
                {
                    // handle initiate
                    await HandleInitiateAsync(swap, receivedSwap)
                        .ConfigureAwait(false);
                }
                else if (IsAccept(swap, receivedSwap) && swap.IsInitiator)
                {
                    // handle accept
                    await HandleAcceptAsync(swap, receivedSwap)
                        .ConfigureAwait(false);
                }
                else if (IsPartyPayment(swap, receivedSwap))
                {
                    // handle party payment
                    await GetCurrencySwap(swap.PurchasedCurrency)
                        .HandlePartyPaymentAsync(swap, receivedSwap)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Existing swap handle error");
            }

            // update swap status
            swap.Status = receivedSwap.Status;
            RaiseSwapUpdated(swap, SwapStateFlags.Empty);
        }

        private bool IsInitiate(ClientSwap swap, ClientSwap receivedSwap)
        {
            return swap.IsStatusSet(receivedSwap.Status, SwapStatus.Initiated);
        }

        private bool IsAccept(ClientSwap swap, ClientSwap receivedSwap)
        {
            return swap.IsStatusSet(receivedSwap.Status, SwapStatus.Accepted);
        }

        private bool IsPartyPayment(ClientSwap swap, ClientSwap receivedSwap)
        {
            var isInitiatorPaymentReceived = swap.IsStatusSet(receivedSwap.Status, SwapStatus.InitiatorPaymentReceived);
            var isAcceptorPaymentReceived = swap.IsStatusSet(receivedSwap.Status, SwapStatus.AcceptorPaymentReceived);

            return swap.IsAcceptor && isInitiatorPaymentReceived ||
                   swap.IsInitiator && isAcceptorPaymentReceived;
        }

        private async Task HandleInitiateAsync(ClientSwap swap, ClientSwap receivedSwap)
        {
            if (swap.SecretHash != null)
            {
                if (!swap.SecretHash.SequenceEqual(receivedSwap.SecretHash))
                    throw new InternalException(
                        code: Errors.InvalidSecretHash,
                        description: $"Secret hash does not match the one already received for swap {swap.Id}");
                return;
            }

            if (receivedSwap.SecretHash == null || receivedSwap.SecretHash.Length != CurrencySwap.DefaultSecretHashSize)
                throw new InternalException(
                    code: Errors.InvalidSecretHash,
                    description: $"Incorrect secret hash length for swap {swap.Id}");

            Log.Debug("Secret hash {@hash} successfully received", receivedSwap.SecretHash.ToHexString());

            swap.SecretHash = receivedSwap.SecretHash;
            RaiseSwapUpdated(swap, SwapStateFlags.HasSecretHash);

            // check party requisites
            if (receivedSwap.PartyAddress == null)
                throw new InternalException(
                    code: Errors.InvalidWallets,
                    description: $"Incorrect party address for swap {swap.Id}");

            //if (IsCriminal(clientSwap.PartyAddress))
            //    throw new InternalException(
            //        code: Errors.IsCriminalWallet,
            //        description: $"Party wallet is criminal for swap {swap.Id}");

            if (receivedSwap.RewardForRedeem < 0)
                throw new InternalException(
                    code: Errors.InvalidRewardForRedeem,
                    description: $"Incorrect reward for redeem for swap {swap.Id}");

            swap.PartyAddress = receivedSwap.PartyAddress;
            swap.PartyRewardForRedeem = receivedSwap.PartyRewardForRedeem;

            // create self requisites
            swap.ToAddress = (await _account
                .GetRedeemAddressAsync(swap.PurchasedCurrency)
                .ConfigureAwait(false))
                .Address;

            swap.RewardForRedeem = await RewardForRedeemAsync(swap)
                .ConfigureAwait(false);

            RaiseSwapUpdated(swap, SwapStateFlags.Empty);

            // send "accept" to other side
            _swapClient.SwapAcceptAsync(swap);

            await GetCurrencySwap(swap.PurchasedCurrency)
                .StartPartyPaymentControlAsync(swap)
                .ConfigureAwait(false);
        }

        private async Task HandleAcceptAsync(ClientSwap swap, ClientSwap receivedSwap)
        {
            // check party requisites
            if (receivedSwap.PartyAddress == null)
                throw new InternalException(
                    code: Errors.InvalidWallets,
                    description: $"Incorrect party address for swap {swap.Id}");

            //if (IsCriminal(clientSwap.PartyAddress))
            //    throw new InternalException(
            //        code: Errors.IsCriminalWallet,
            //        description: $"Party wallet is criminal for swap {swap.Id}");

            if (receivedSwap.RewardForRedeem < 0)
                throw new InternalException(
                    code: Errors.InvalidRewardForRedeem,
                    description: $"Incorrect reward for redeem for swap {swap.Id}");

            swap.PartyAddress = receivedSwap.PartyAddress;
            swap.PartyRewardForRedeem = receivedSwap.PartyRewardForRedeem;
            RaiseSwapUpdated(swap, SwapStateFlags.Empty);

            // broadcast initiator payment
            await GetCurrencySwap(swap.SoldCurrency)
                .PayAsync(swap)
                .ConfigureAwait(false);

            await GetCurrencySwap(swap.PurchasedCurrency)
                .StartPartyPaymentControlAsync(swap)
                .ConfigureAwait(false);
        }

        public async Task RestoreSwapsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var swaps = await _account
                    .GetSwapsAsync()
                    .ConfigureAwait(false);

                foreach (var swap in swaps.Where(s => s.IsActive))
                {
                    try
                    {
                        await RestoreSwapAsync(swap, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "Swap {@id} restore error", swap.Id);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Swaps restore error");
            }
        }

        private async Task RestoreSwapAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
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
            }
            else
            {
                if (DateTime.UtcNow < swap.TimeStamp.ToUniversalTime() + DefaultMaxSwapTimeout)
                {
                    if (swap.IsInitiator)
                    {
                        // todo: reinitiate swap
                    }
                    else
                    {
                        // todo: reaccept swap
                    }
                }
                else
                {
                    swap.Cancel();
                    RaiseSwapUpdated(swap, SwapStateFlags.IsCanceled);
                }
            }
        }

        private void RaiseSwapUpdated(ClientSwap swap, SwapStateFlags changedFlag)
        {
            SwapUpdatedHandler(this, new SwapEventArgs(swap, changedFlag));
        }

        private async void SwapUpdatedHandler(
            object sender,
            SwapEventArgs args)
        {
            try
            {
                Log.Debug("Update swap {@swap} in db", args.Swap.Id);

                var result = await _account
                    .UpdateSwapAsync(args.Swap)
                    .ConfigureAwait(false);

                if (!result)
                    Log.Error("Swap update error");

                //_account.AssetWarrantyManager.Alloc()

                SwapUpdated?.Invoke(this, args);
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap update error");
            }
        }

        private async void InitiatorPaymentConfirmed(
            ICurrencySwap currencySwap,
            SwapEventArgs swapArgs)
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
                        .PayAsync(swap)
                        .ConfigureAwait(false);

                    // wait for redeem by other party or someone else
                    if (swap.RewardForRedeem > 0)
                        await GetCurrencySwap(swap.PurchasedCurrency)
                            .StartWaitForRedeemBySomeoneAsync(swap)
                            .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Initiator payment confirmed handler error");
            }
        }

        private async void AcceptorPaymentConfirmed(
            ICurrencySwap currencySwap,
            SwapEventArgs swapArgs)
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
                        .RedeemForPartyAsync(swap)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Acceptor payment confirmed handler error");
            }
        }

        private async void AcceptorPaymentSpent(
            ICurrencySwap currencySwap,
            SwapEventArgs swapArgs)
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
                        .RedeemAsync(swap)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Acceptor payment spent handler error");
            }
        }

        private async Task LockSwapAsync(long swapId)
        {
            if (_semaphores.TryGetValue(swapId, out SemaphoreSlim semaphore))
            {
                await semaphore
                    .WaitAsync()
                    .ConfigureAwait(false);
            }
            else
            {
                semaphore = new SemaphoreSlim(1, 1);

                await semaphore
                    .WaitAsync()
                    .ConfigureAwait(false);

                if (!_semaphores.TryAdd(swapId, semaphore))
                {
                    semaphore.Release();
                    semaphore.Dispose();

                    if (_semaphores.TryGetValue(swapId, out semaphore))
                    {
                        await semaphore
                            .WaitAsync()
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
            if (_semaphores.TryGetValue(id, out var semaphore))
                semaphore.Release();
        }

        private async Task<decimal> RewardForRedeemAsync(ClientSwap swap)
        {
            if (swap.PurchasedCurrency is BitcoinBasedCurrency)
                return 0;

            var purchasedCurrencyBalance = await _account
                .GetBalanceAsync(swap.PurchasedCurrency)
                .ConfigureAwait(false);

            return purchasedCurrencyBalance.Available < swap.PurchasedCurrency.GetDefaultRedeemFee()
                ? swap.PurchasedCurrency.GetDefaultRedeemFee() * 2
                : 0;
        }
    }
}