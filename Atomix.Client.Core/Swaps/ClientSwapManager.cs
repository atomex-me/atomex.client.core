using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomix.Common;
using Atomix.Common.Abstract;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.Swaps.Abstract;
using Atomix.Wallet.Abstract;
using Serilog;

namespace Atomix.Swaps
{
    public class ClientSwapManager
    {
        public event EventHandler<SwapEventArgs> SwapUpdated;

        private readonly IAccount _account;
        private readonly ISwapClient _swapClient;
        private readonly IDictionary<string, ICurrencySwap> _swaps;

        public ClientSwapManager(
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _swapClient = swapClient ?? throw new ArgumentNullException(nameof(swapClient));

            _swaps = _account.Currencies
                .Select(c =>
                {
                    var currencySwap = CurrencySwapCreator.Create(
                        currency: c,
                        account: _account,
                        swapClient: swapClient,
                        taskPerformer: taskPerformer);

                    currencySwap.InitiatorPaymentConfirmed += InitiatorPaymentConfirmed;
                    currencySwap.AcceptorPaymentConfirmed += AcceptorPaymentConfirmed;
                    currencySwap.AcceptorPaymentSpent += AcceptorPaymentSpent;
                    currencySwap.SwapUpdated += SwapUpdatedHandler;

                    return currencySwap;
                })
                .ToDictionary(cs => cs.Currency.Name);
        }

        private ICurrencySwap GetCurrencySwap(Currency currency) => _swaps[currency.Name];

        public async Task HandleSwapAsync(ClientSwap clientSwap)
        {
            var swap = await _account
                .GetSwapByIdAsync(clientSwap.Id)
                .ConfigureAwait(false);

            if (swap == null)
            {
                await RunSwapAsync(clientSwap)
                    .ConfigureAwait(false);
            }
            else
            {
                await HandleExistingSwapAsync(swap, clientSwap)
                    .ConfigureAwait(false);
            }
        }

        private async Task RunSwapAsync(ClientSwap clientSwap)
        {
            //var order = _account.GetOrderById(clientSwap.Order.ClientOrderId);

            //if (order == null || !clientSwap.Order.IsContinuationOf(order))
            //{
            //    Log.Warning("Probably swap {@swapId} created on another device", clientSwap.Id);
            //    return;
            //}

            var swap = new ClientSwap
            {
                Id = clientSwap.Id,
                Status = clientSwap.Status,
                TimeStamp = clientSwap.TimeStamp,
                Symbol = clientSwap.Symbol,
                Side = clientSwap.Side,
                Price = clientSwap.Price,
                Qty = clientSwap.Qty,
                IsInitiative = clientSwap.IsInitiative,
            };

            var result = await _account
                .AddSwapAsync(swap)
                .ConfigureAwait(false);

            if (!result)
            {
                Log.Error("Can't add swap {@swapId} to account swaps repository", clientSwap.Id);
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

            var purchasedCurrencyBalance = await _account
                .GetBalanceAsync(swap.PurchasedCurrency)
                .ConfigureAwait(false);

            swap.RewardForRedeem = purchasedCurrencyBalance.Available < swap.PurchasedCurrency.GetDefaultRedeemFee() &&
                                   !(swap.PurchasedCurrency is BitcoinBasedCurrency)
                ? swap.PurchasedCurrency.GetDefaultRedeemFee() * 2
                : 0;
            RaiseSwapUpdated(swap, SwapStateFlags.Empty);

            _swapClient.SwapInitiateAsync(swap);
        }

        private async Task HandleExistingSwapAsync(ClientSwap swap, ClientSwap clientSwap)
        {
            try
            {
                if (IsInitiate(swap, clientSwap) && swap.IsAcceptor)
                {
                    // handle initiate
                    await HandleInitiateAsync(swap, clientSwap)
                        .ConfigureAwait(false);
                }
                else if (IsAccept(swap, clientSwap) && swap.IsInitiator)
                {
                    // handle accept
                    await HandleAcceptAsync(swap, clientSwap)
                        .ConfigureAwait(false);
                }
                else if (IsPartyPayment(swap, clientSwap))
                {
                    // party payment
                    await GetCurrencySwap(swap.PurchasedCurrency)
                        .HandlePartyPaymentAsync(swap, clientSwap)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Existing swap handle error");
            }

            // update swap status
            swap.Status = clientSwap.Status;
            RaiseSwapUpdated(swap, SwapStateFlags.Empty);
        }

        private bool IsInitiate(ClientSwap swap, ClientSwap clientSwap)
        {
            return swap.IsStatusChanged(clientSwap.Status, SwapStatus.Initiated);
        }

        private bool IsAccept(ClientSwap swap, ClientSwap clientSwap)
        {
            return swap.IsStatusChanged(clientSwap.Status, SwapStatus.Accepted);
        }

        private bool IsPartyPayment(ClientSwap swap, ClientSwap clientSwap)
        {
            var isInitiatorPaymentReceived = swap.IsStatusChanged(clientSwap.Status, SwapStatus.InitiatorPaymentReceived);
            var isAcceptorPaymentReceived = swap.IsStatusChanged(clientSwap.Status, SwapStatus.AcceptorPaymentReceived);

            return swap.IsAcceptor && isInitiatorPaymentReceived ||
                   swap.IsInitiator && isAcceptorPaymentReceived;
        }

        private async Task HandleInitiateAsync(ClientSwap swap, ClientSwap clientSwap)
        {
            if (swap.SecretHash != null)
            {
                if (!swap.SecretHash.SequenceEqual(clientSwap.SecretHash))
                    throw new InternalException(
                        code: Errors.InvalidSecretHash,
                        description: $"Secret hash does not match the one already received for swap {swap.Id}");
                return;
            }

            if (clientSwap.SecretHash == null || clientSwap.SecretHash.Length != CurrencySwap.DefaultSecretHashSize)
                throw new InternalException(
                    code: Errors.InvalidSecretHash,
                    description: $"Incorrect secret hash length for swap {swap.Id}");

            Log.Debug("Secret hash {@hash} successfully received", clientSwap.SecretHash.ToHexString());

            swap.SecretHash = clientSwap.SecretHash;
            RaiseSwapUpdated(swap, SwapStateFlags.HasSecretHash);

            // check party requisites
            if (clientSwap.PartyAddress == null)           
                throw new InternalException(
                    code: Errors.InvalidWallets,
                    description: $"Incorrect party address for swap {swap.Id}");

            //if (IsCriminal(clientSwap.PartyAddress))
            //    throw new InternalException(
            //        code: Errors.IsCriminalWallet,
            //        description: $"Party wallet is criminal for swap {swap.Id}");

            if (clientSwap.RewardForRedeem < 0)
                throw new InternalException(
                    code: Errors.InvalidRewardForRedeem,
                    description: $"Incorrect reward for redeem for swap {swap.Id}");

            swap.PartyAddress = clientSwap.PartyAddress;
            swap.PartyRewardForRedeem = clientSwap.PartyRewardForRedeem;
            
            // create self requisites
            swap.ToAddress = (await _account
                .GetRedeemAddressAsync(swap.PurchasedCurrency)
                .ConfigureAwait(false))
                .Address;

            var purchasedCurrencyBalance = await _account
                .GetBalanceAsync(swap.PurchasedCurrency)
                .ConfigureAwait(false);

            swap.RewardForRedeem = purchasedCurrencyBalance.Available < swap.PurchasedCurrency.GetDefaultRedeemFee() &&
                                   !(swap.PurchasedCurrency is BitcoinBasedCurrency)
                ? swap.PurchasedCurrency.GetDefaultRedeemFee() * 2
                : 0;

            RaiseSwapUpdated(swap, SwapStateFlags.Empty);

            // send "accept" to other side
            _swapClient.SwapAcceptAsync(swap);

            await GetCurrencySwap(swap.PurchasedCurrency)
                .PrepareToReceiveAsync(swap)
                .ConfigureAwait(false);
        }

        private async Task HandleAcceptAsync(ClientSwap swap, ClientSwap clientSwap)
        {
            // check party requisites
            if (clientSwap.PartyAddress == null)
                throw new InternalException(
                    code: Errors.InvalidWallets,
                    description: $"Incorrect party address for swap {swap.Id}");

            //if (IsCriminal(clientSwap.PartyAddress))
            //    throw new InternalException(
            //        code: Errors.IsCriminalWallet,
            //        description: $"Party wallet is criminal for swap {swap.Id}");

            if (clientSwap.RewardForRedeem < 0)
                throw new InternalException(
                    code: Errors.InvalidRewardForRedeem,
                    description: $"Incorrect reward for redeem for swap {swap.Id}");

            swap.PartyAddress = clientSwap.PartyAddress;
            swap.PartyRewardForRedeem = clientSwap.PartyRewardForRedeem;
            RaiseSwapUpdated(swap, SwapStateFlags.Empty);

            // broadcast initiator payment
            await GetCurrencySwap(swap.SoldCurrency)
                .BroadcastPaymentAsync(swap)
                .ConfigureAwait(false);

            await GetCurrencySwap(swap.PurchasedCurrency)
                .PrepareToReceiveAsync(swap)
                .ConfigureAwait(false);
        }

        public async Task RestoreSwapsAsync()
        {
            try
            {
                var swaps = await _account
                    .GetSwapsAsync()
                    .ConfigureAwait(false);

                foreach (var swap in swaps)
                {
                    if (swap.IsComplete || swap.IsCanceled || swap.IsRefunded)
                        continue;

                    try
                    {
                        await GetCurrencySwap(swap.SoldCurrency)
                            .RestoreSwapAsync(swap)
                            .ConfigureAwait(false);

                        await GetCurrencySwap(swap.PurchasedCurrency)
                            .RestoreSwapAsync(swap)
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

        protected void RaiseSwapUpdated(ClientSwap swap, SwapStateFlags changedFlag)
        {
            SwapUpdatedHandler(this, new SwapEventArgs(swap, changedFlag));
        }

        private async void SwapUpdatedHandler(object sender, SwapEventArgs args)
        {
            try
            {
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

        private async void InitiatorPaymentConfirmed(
            ICurrencySwap currencySwap,
            SwapEventArgs swapArgs)
        {
            var swap = swapArgs.Swap;

            Log.Debug("Initiator payment confirmed event for swap {@swapId}", swap.Id);

            try
            {
                // broadcast acceptor's payment tx (using sold currency protocol)
                if (swap.IsAcceptor &&
                    swap.IsPurchasedCurrency(currencySwap.Currency))
                {
                    await GetCurrencySwap(swap.SoldCurrency)
                        .BroadcastPaymentAsync(swap)
                        .ConfigureAwait(false);

                    // wait for redeem by other party or someone else
                    if (swap.RewardForRedeem > 0)
                        await GetCurrencySwap(swap.PurchasedCurrency)
                            .WaitForRedeemAsync(swap)
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
                        .PartyRedeemAsync(swap)
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
    }
}