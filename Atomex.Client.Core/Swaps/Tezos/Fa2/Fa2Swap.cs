using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Netezos.Encoding;
using Netezos.Forging.Models;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.Helpers;
using Atomex.Swaps.Tezos.Fa2.Helpers;
using Atomex.TezosTokens;
using Atomex.Wallet.Tezos;

namespace Atomex.Swaps.Tezos.Fa2
{
    public class Fa2Swap : CurrencySwap
    {
        private Fa2Account Fa2Account { get; }
        private TezosAccount TezosAccount { get; }
        private Fa2Config Fa2Config => Currencies.Get<Fa2Config>(Currency);
        private TezosConfig XtzConfig => Currencies.Get<TezosConfig>(TezosAccount.Currency);
        public static TimeSpan InitiationTimeout = TimeSpan.FromMinutes(10);
        public static TimeSpan InitiationCheckInterval = TimeSpan.FromSeconds(15);

        public Fa2Swap(
            Fa2Account account,
            TezosAccount tezosAccount,
            ICurrencies currencies)
            : base(account.Currency, currencies)
        {
            Fa2Account = account ?? throw new ArgumentNullException(nameof(account));
            TezosAccount = tezosAccount ?? throw new ArgumentNullException(nameof(account));
        }

        public override async Task PayAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            if (!await CheckPayRelevanceAsync(swap, cancellationToken))
                return;

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            Log.Debug("Create {@currency} payment transaction from address {@address} for swap {@swapId}",
                Currency,
                swap.FromAddress,
                swap.Id);

            var fa2 = Fa2Config;
            var requiredAmountInTokens = GetRequiredAmount(swap, fa2);
            var requiredAmountInTokenDigits = requiredAmountInTokens.ToTokens(fa2.Decimals);

            var refundTimeStampUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds)).ToUnixTimeSeconds();
            var rewardForRedeemInTokenDigits = swap.IsInitiator
                ? swap.PartyRewardForRedeem.ToTokens(fa2.Decimals)
                : 0;

            var walletAddress = await Fa2Account
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            var amountInTokenDigits = AmountHelper.DustProofMin(
                walletAddress.Balance,
                requiredAmountInTokenDigits,
                fa2.DustDigitsMultiplier);

            var balanceInMtz = (await TezosAccount
                .GetAddressBalanceAsync(
                    address: walletAddress.Address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .Confirmed;

            var isRevealed = await TezosAccount
                .IsRevealedSourceAsync(walletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            var tzktApi = new TzktApi(XtzConfig.GetTzktSettings());

            var (isActive, isActiveError) = await tzktApi
                .IsFa2TokenOperatorActiveAsync(
                    holderAddress: walletAddress.Address,
                    spenderAddress: fa2.SwapContractAddress,
                    tokenContractAddress: fa2.TokenContractAddress,
                    tokenId: fa2.TokenId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (isActiveError != null)
            {
                Log.Error("Get fa2 token operator active error");
                return;
            }

            var feeAmountInMtz = (!isActive ? fa2.ApproveFee : 0) +
                (!isRevealed ? fa2.RevealFee : 0) +
                fa2.InitiateFee;

            var storageLimit = (!isActive ? fa2.ApproveStorageLimit : 0) + fa2.InitiateStorageLimit;
            var storageLimitInMtz = storageLimit * fa2.StorageFeeMultiplier;

            if (balanceInMtz < feeAmountInMtz + storageLimitInMtz + XtzConfig.MicroTezReserve)
            {
                Log.Error(
                    "Insufficient funds at {@address} for fee. Balance: {@balance}, " +
                    "feeAmount: {@feeAmount}, storageLimit: {@storageLimit}, missing: {@missing}.",
                    walletAddress.Address,
                    balanceInMtz,
                    feeAmountInMtz,
                    storageLimitInMtz,
                    balanceInMtz - feeAmountInMtz - storageLimitInMtz - XtzConfig.MicroTezReserve);

                return;
            }

            Log.Debug("Available balance: {@balance}", walletAddress.Balance);

            if (walletAddress.Balance < requiredAmountInTokenDigits)
            {
                Log.Error(
                    "Insufficient funds at {@address}. Balance: {@balance}, " +
                    "required: {@required}, missing: {@missing}.",
                    walletAddress.Address,
                    walletAddress.Balance,
                    requiredAmountInTokenDigits,
                    walletAddress.Balance - requiredAmountInTokenDigits);

                return;
            }

            var txs = new List<TezosOperationParameters>();

            if (!isActive)
            {
                // approve
                txs.Add(new TezosOperationParameters
                {
                    Content = new TransactionContent
                    {
                        Amount       = 0,
                        Source       = walletAddress.Address,
                        Destination  = fa2.TokenContractAddress,
                        Fee          = fa2.ApproveFee,
                        GasLimit     = (int)fa2.ApproveGasLimit,
                        StorageLimit = (int)fa2.ApproveStorageLimit,
                        Parameters   = new Parameters
                        {
                            Entrypoint = "update_operators",
                            Value = Micheline.FromJson(GetApproveParameters(
                                owner: walletAddress.Address,
                                spender: fa2.SwapContractAddress,
                                tokenId: fa2.TokenId))
                        }
                    },
                    From         = walletAddress.Address,
                    Fee          = Fee.FromNetwork(fa2.ApproveFee),
                    GasLimit     = GasLimit.FromValue((int)fa2.ApproveGasLimit),
                    StorageLimit = StorageLimit.FromValue((int)fa2.ApproveStorageLimit)
                });
            }

            // initiate
            txs.Add(new TezosOperationParameters
            {
                Content = new TransactionContent
                {
                    Amount       = 0,
                    Source       = walletAddress.Address,
                    Destination  = fa2.SwapContractAddress,
                    Fee          = feeAmountInMtz,
                    GasLimit     = (int)fa2.InitiateGasLimit,
                    StorageLimit = (int)fa2.InitiateStorageLimit,
                    Parameters = new Parameters
                    {
                        Entrypoint = "initiate",
                        Value = Micheline.FromJson(GetInitiateParameters(
                            swap: swap,
                            tokenContractAddress: fa2.TokenContractAddress,
                            tokenId: fa2.TokenId,
                            tokenAmountInDigits: amountInTokenDigits,
                            refundTimeStamp: refundTimeStampUtcInSec,
                            redeemFeeAmount: rewardForRedeemInTokenDigits))
                    }
                },
                From         = walletAddress.Address,
                Fee          = Fee.FromNetwork(feeAmountInMtz),
                GasLimit     = GasLimit.FromValue((int)fa2.InitiateGasLimit),
                StorageLimit = StorageLimit.FromValue((int)fa2.InitiateStorageLimit)
            });

            var (result, error) = await TezosAccount
                .SendTransactionsAsync(
                    transactions: txs,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                Log.Error($"Error while send Fa2 initiate transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }

            if (result.Error != null)
            {
                Log.Error($"Error while send Fa2 initiate transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }

            swap.PaymentTxId = result.OperationId;
            swap.StateFlags |= SwapStateFlags.IsPaymentSigned | SwapStateFlags.IsPaymentBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentSigned | SwapStateFlags.IsPaymentBroadcast, cancellationToken)
                .ConfigureAwait(false); ;

            var isInitiateConfirmed = await WaitPaymentConfirmationAsync(
                    txId: result.OperationId,
                    timeout: InitiationTimeout,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
                    
            if (!isInitiateConfirmed)
            {
                Log.Error("Initiation payment tx not confirmed after timeout {@timeout}",
                    InitiationTimeout.Minutes);
                return;
            }
                
            swap.StateFlags |= SwapStateFlags.IsPaymentConfirmed;

            await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentConfirmed, cancellationToken)
                .ConfigureAwait(false);
        }

        public override Task StartPartyPaymentControlAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Start party {@currency} payment control for swap {@swap}", Currency, swap.Id);

            // initiator waits "accepted" event, acceptor waits "initiated" event
            var initiatedHandler = swap.IsInitiator
                ? new Func<Swap, CancellationToken, Task>(SwapAcceptedHandler)
                : new Func<Swap, CancellationToken, Task>(SwapInitiatedHandler);

            var lockTimeSeconds = swap.IsInitiator
                ? DefaultAcceptorLockTimeInSeconds
                : DefaultInitiatorLockTimeInSeconds;

            var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeSeconds)).ToUnixTimeSeconds();

            _ = Task.Run(() => Fa2SwapInitiatedHelper.StartSwapInitiatedControlAsync(
                swap: swap,
                currency: Fa2Config,
                tezos: XtzConfig,
                refundTimeStamp: refundTimeUtcInSec,
                interval: ConfirmationCheckInterval,
                initiatedHandler: initiatedHandler,
                canceledHandler: SwapCanceledHandler,
                cancellationToken: cancellationToken), cancellationToken);

            return Task.CompletedTask;
        }

        public override async Task RedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var fa2 = Fa2Config;
            
            var (secret, isRedeemedError) = await Fa2SwapRedeemedHelper
                .IsRedeemedAsync(
                    swap: swap,
                    currency: fa2,
                    tezos: XtzConfig,
                    attempts: TezosSwap.MaxRedeemCheckAttempts,
                    attemptIntervalInSec: TezosSwap.RedeemCheckAttemptIntervalInSec,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (isRedeemedError == null && secret != null)
            {
                await RedeemConfirmedEventHandler(swap, cancellationToken)
                    .ConfigureAwait(false);

                return;
            }

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast) &&
                swap.LastRedeemTryTimeStamp + TimeSpan.FromMinutes(5) > DateTime.UtcNow)
            {
                // redeem already broadcast
                _ = TrackTransactionConfirmationAsync<TezosOperation>(
                    swap: swap,
                    localStorage: Fa2Account.LocalStorage,
                    txId: swap.RedeemTxId,
                    confirmationHandler: RedeemConfirmedEventHandler,
                    cancellationToken: cancellationToken);

                return;
            }

            // check already refunded by initiator
            if (swap.IsAcceptor &&
                swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultInitiatorLockTimeInSeconds) < DateTime.UtcNow)
            {
                var (isRefundedByParty, isRefundedError) = await Fa2SwapRefundedHelper
                    .IsRefundedAsync(swap, fa2, XtzConfig, cancellationToken)
                    .ConfigureAwait(false);

                if (isRefundedError == null && isRefundedByParty)
                {
                    swap.StateFlags |= SwapStateFlags.IsUnsettled;

                    await UpdateSwapAsync(swap, SwapStateFlags.IsUnsettled, cancellationToken)
                        .ConfigureAwait(false);

                    return;
                }
            }

            if (swap.IsInitiator)
            {
                var redeemDeadline = swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds) - RedeemTimeReserve;

                if (DateTime.UtcNow > redeemDeadline)
                {
                    Log.Error("Redeem dedline reached for swap {@swap}", swap.Id);
                    return;
                }
            }

            Log.Debug("Create redeem for swap {@swapId}", swap.Id);

            var walletAddress = await TezosAccount
                .GetAddressAsync(swap.RedeemFromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for redeem from local db", swap.RedeemFromAddress);
                return;
            }

            if (walletAddress == null)
            {
                Log.Error("Insufficient funds for redeem");
                return;
            }

            var feeAmountInMtz = fa2.RedeemFee + fa2.RevealFee;
            var storageLimitInMtz = fa2.RedeemStorageLimit * fa2.StorageFeeMultiplier;

            if (walletAddress.Balance < feeAmountInMtz + storageLimitInMtz)
            {
                Log.Error("Insufficient funds for redeem");
                return;
            }

            var (result, error) = await TezosAccount
                .SendTransactionAsync(
                    from: walletAddress.Address,
                    to: fa2.SwapContractAddress,
                    amount: 0,
                    fee: Fee.FromNetwork(fa2.RedeemFee + fa2.RevealFee),
                    gasLimit: GasLimit.FromValue((int)fa2.RedeemGasLimit),
                    storageLimit: StorageLimit.FromValue((int)fa2.RedeemStorageLimit),
                    entrypoint: "redeem",
                    parameters: GetRedeemParameters(swap),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                Log.Error($"Error while send Fa2 redeem transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }

            if (result.Error != null)
            {
                Log.Error($"Error while send Fa2 redeem transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }

            swap.RedeemTxId = result.OperationId;
            swap.LastRedeemTryTimeStamp = DateTime.UtcNow;
            swap.StateFlags |= SwapStateFlags.IsRedeemSigned | SwapStateFlags.IsRedeemBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemSigned | SwapStateFlags.IsRedeemBroadcast, cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync<TezosOperation>(
                swap: swap,
                localStorage: Fa2Account.LocalStorage,
                txId: result.OperationId,
                confirmationHandler: RedeemConfirmedEventHandler,
                cancellationToken: cancellationToken);
        }

        public override async Task RedeemForPartyAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.IsInitiator)
            {
                var partyRedeemDeadline = swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds) - PartyRedeemTimeReserve;

                if (DateTime.UtcNow > partyRedeemDeadline)
                {
                    Log.Error("Party redeem deadline reached for swap {@swap}", swap.Id);
                    return;
                }
            }

            Log.Debug("Create redeem for acceptor for swap {@swapId}", swap.Id);

            var fa2 = Fa2Config;

            var walletAddress = await TezosAccount
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for redeem for party from local db", swap.FromAddress);
                return;
            }

            var feeAmountInMtz = fa2.RedeemFee + fa2.RevealFee;
            var storageLimitInMtz = fa2.RedeemStorageLimit * fa2.StorageFeeMultiplier;

            if (walletAddress.Balance < feeAmountInMtz + storageLimitInMtz)
            {
                Log.Error("Insufficient funds for redeem for party");
            }

            var (result, error) = await TezosAccount
                .SendTransactionAsync(
                    from: walletAddress.Address,
                    to: fa2.SwapContractAddress,
                    amount: 0,
                    fee: Fee.FromNetwork(fa2.RedeemFee + fa2.RevealFee),
                    gasLimit: GasLimit.FromValue((int)fa2.RedeemGasLimit),
                    storageLimit: StorageLimit.FromValue((int)fa2.RedeemStorageLimit),
                    entrypoint: "redeem",
                    parameters: GetRedeemParameters(swap),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                Log.Error($"Error while send Fa2 redeem for party transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }

            if (result.Error != null)
            {
                Log.Error($"Error while send Fa2 redeem fort party transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }
        }

        public override async Task RefundAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var fa2 = Fa2Config;

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast) &&
                swap.LastRefundTryTimeStamp + TimeSpan.FromMinutes(5) > DateTime.UtcNow)
            {
                _ = TrackTransactionConfirmationAsync<TezosOperation>(
                    swap: swap,
                    localStorage: Fa2Account.LocalStorage,
                    txId: swap.RefundTxId,
                    confirmationHandler: RefundConfirmedEventHandler,
                    cancellationToken: cancellationToken);

                return;
            }

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            var lockTime = swap.TimeStamp.ToUniversalTime() + TimeSpan.FromSeconds(lockTimeInSeconds);

            await RefundTimeDelayAsync(lockTime, cancellationToken)
                .ConfigureAwait(false);

            Log.Debug("Create refund for swap {@swap}", swap.Id);

            var walletAddress = await TezosAccount
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for refund from local db", swap.FromAddress);
                return;
            }

            var feeAmountInMtz = fa2.RefundFee + fa2.RevealFee;
            var storageLimitInMtz = fa2.RefundStorageLimit * fa2.StorageFeeMultiplier;

            if (walletAddress.Balance < feeAmountInMtz + storageLimitInMtz)
            {
                Log.Error("Insufficient funds for refund");
            }

            var (result, error) = await TezosAccount
                .SendTransactionAsync(
                    from: walletAddress.Address,
                    to: fa2.SwapContractAddress,
                    amount: 0,
                    fee: Fee.FromNetwork(fa2.RefundFee + fa2.RevealFee),
                    gasLimit: GasLimit.FromValue((int)fa2.RefundGasLimit),
                    storageLimit: StorageLimit.FromValue((int)fa2.RefundStorageLimit),
                    entrypoint: "refund",
                    parameters: GetRefundParameters(swap),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                Log.Error($"Error while send Fa2 refund transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }

            if (result.Error != null)
            {
                Log.Error($"Error while send Fa2 refund transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }

            swap.RefundTxId = result.OperationId;
            swap.LastRefundTryTimeStamp = DateTime.UtcNow;
            swap.StateFlags |= SwapStateFlags.IsRefundSigned | SwapStateFlags.IsRefundBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRefundSigned | SwapStateFlags.IsRefundBroadcast, cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync<TezosOperation>(
                swap: swap,
                localStorage: Fa2Account.LocalStorage,
                txId: result.OperationId,
                confirmationHandler: RefundConfirmedEventHandler,
                cancellationToken: cancellationToken);
        }

        public override Task StartWaitingForRedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Start waiting for {@currency} redeem for swap {@swap}", Currency, swap.Id);

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            // start redeem control async
            _ = Task.Run(() => Fa2SwapRedeemedHelper.StartSwapRedeemedControlAsync(
                swap: swap,
                currency: Fa2Config,
                tezos: XtzConfig,
                refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds),
                interval: TimeSpan.FromSeconds(30),
                redeemedHandler: RedeemCompletedEventHandler,
                canceledHandler: RedeemCanceledEventHandler,
                cancellationToken: cancellationToken), cancellationToken);

            return Task.CompletedTask;
        }

        public override Task StartWaitForRedeemBySomeoneAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Wait redeem for swap {@swapId}", swap.Id);

            // start redeem control async
            _ = Task.Run(() => Fa2SwapRedeemedHelper.StartSwapRedeemedControlAsync(
                swap: swap,
                currency: Fa2Config,
                tezos: XtzConfig,
                refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds),
                interval: TimeSpan.FromSeconds(30),
                redeemedHandler: RedeemBySomeoneCompletedEventHandler,
                canceledHandler: RedeemBySomeoneCanceledEventHandler,
                cancellationToken: cancellationToken), cancellationToken);

            return Task.CompletedTask;
        }

        public override async Task<Result<ITransaction>> TryToFindPaymentAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var currency = Currencies
                .GetByName(swap.SoldCurrency);

            return await Fa2SwapInitiatedHelper
                .TryToFindPaymentAsync(
                    swap: swap,
                    currency: currency,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        #region Event Handlers

        protected override async Task RefundTimeReachedHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Refund time reached for swap {@swapId}", swap.Id);

            try
            {
                var (isRefunded, error) = await Fa2SwapRefundedHelper
                    .IsRefundedAsync(
                        swap: swap,
                        currency: Fa2Config,
                        tezos: XtzConfig,
                        attempts: TezosSwap.MaxRefundCheckAttempts,
                        attemptIntervalInSec: TezosSwap.RefundCheckAttemptIntervalInSec,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error == null)
                {
                    if (isRefunded)
                    {
                        await RefundConfirmedEventHandler(swap, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await RefundAsync(swap, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in refund time reached handler");
            }
        }

        private async Task RedeemBySomeoneCompletedEventHandler(
            Swap swap,
            byte[] secret,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem party control completed event for swap {@swapId}", swap.Id);

            if (swap.IsAcceptor)
            {
                swap.Secret = secret;
                swap.StateFlags |= SwapStateFlags.IsRedeemConfirmed;

                await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemConfirmed, cancellationToken)
                    .ConfigureAwait(false);

                // get transactions & update balance for address async 
                _ = AddressHelper.UpdateAddressBalanceAsync<TezosTokensWalletScanner, TezosAccount>(
                    account: TezosAccount,
                    address: swap.ToAddress,
                    cancellationToken: cancellationToken);
            }
        }

        private async Task RedeemBySomeoneCanceledEventHandler(
            Swap swap,
            DateTime refundTimeUtc,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem party control canceled event for swap {@swapId}", swap.Id);

            try
            {
                if (swap.Secret?.Length > 0)
                {
                    await RedeemAsync(swap, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Redeem party control canceled event error");
            }
        }

        #endregion Event Handlers

        #region Helpers
        
        private async Task<bool> WaitPaymentConfirmationAsync(
            string txId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var timeStamp = DateTime.UtcNow;

            while (DateTime.UtcNow < timeStamp + timeout)
            {
                await Task.Delay(InitiationCheckInterval, cancellationToken)
                    .ConfigureAwait(false);

                var tx = await Fa2Account
                    .LocalStorage
                    .GetTransactionByIdAsync<TezosOperation>(XtzConfig.Name, txId)
                    .ConfigureAwait(false);

                if (tx is not { IsConfirmed: true }) continue;

                return tx.IsConfirmed;
            }

            return false;
        }

        public static decimal GetRequiredAmount(Swap swap, Fa2Config fa2)
        {
            var requiredAmount = AmountHelper.QtyToSellAmount(swap.Side, swap.Qty, swap.Price, fa2.Precision);

            // maker network fee
            if (swap.MakerNetworkFee > 0 && swap.MakerNetworkFee < requiredAmount) // network fee size check
                requiredAmount += AmountHelper.RoundDown(swap.MakerNetworkFee, fa2.Precision);

            return requiredAmount;
        }

        private string GetApproveParameters(
            string owner,
            string spender,
            BigInteger tokenId)
        {
            return $"[{{\"prim\":\"Left\",\"args\":[{{\"prim\":\"Pair\",\"args\":[{{\"string\":\"{owner}\"}},{{\"prim\":\"Pair\",\"args\":[{{\"string\":\"{spender}\"}},{{\"int\":\"{tokenId}\"}}]}}]}}]}}]";
        }

        private string GetInitiateParameters(
            Swap swap,
            string tokenContractAddress,
            BigInteger tokenId,
            BigInteger tokenAmountInDigits,
            long refundTimeStamp,
            BigInteger redeemFeeAmount)
        {
            return $"{{\"prim\":\"Pair\",\"args\":[{{\"prim\":\"Pair\",\"args\":[{{\"prim\":\"Pair\",\"args\":[{{\"bytes\":\"{swap.SecretHash.ToHexString()}\"}},{{\"string\":\"{swap.PartyAddress}\"}}]}},{{\"prim\":\"Pair\",\"args\":[{{\"int\":\"{redeemFeeAmount}\"}},{{\"int\":\"{refundTimeStamp}\"}}]}}]}},{{\"prim\":\"Pair\",\"args\":[{{\"prim\":\"Pair\",\"args\":[{{\"string\":\"{tokenContractAddress}\"}},{{\"int\":\"{tokenId}\"}}]}},{{\"int\":\"{tokenAmountInDigits}\"}}]}}]}}";
        }

        private string GetRedeemParameters(Swap swap)
        {
            return $"{{\"bytes\":\"{swap.Secret.ToHexString()}\"}}";
        }

        private string GetRefundParameters(Swap swap)
        {
            return $"{{\"bytes\":\"{swap.SecretHash.ToHexString()}\"}}";
        }

        #endregion Helpers
    }
}