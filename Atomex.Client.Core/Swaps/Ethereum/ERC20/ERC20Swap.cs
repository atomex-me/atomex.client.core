using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Threading;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.ERC20;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.Ethereum.ERC20.Helpers;
using Atomex.Swaps.Helpers;
using Atomex.Wallet.Ethereum;
using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Serilog;
using Atomex.Abstract;

namespace Atomex.Swaps.Ethereum
{
    public class ERC20Swap : EthereumSwap
    {
        private ERC20Account Erc20Account => _account as ERC20Account;
        private EthereumAccount EthereumAccount { get; }
        private EthereumTokens.ERC20 Erc20 => Currencies.Get<EthereumTokens.ERC20>(Currency);
        private Atomex.Ethereum Eth => Currencies.Get<Atomex.Ethereum>(EthereumAccount.Currency);

        public ERC20Swap(
            ERC20Account account,
            EthereumAccount ethereumAccount,
            ISwapClient swapClient,
            ICurrencies currencies)
            : base(account, swapClient, currencies)
        {
            EthereumAccount = ethereumAccount ?? throw new ArgumentNullException(nameof(account));
        }

        public override async Task PayAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            if (!CheckPayRelevance(swap))
                return;

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            var txs = (await CreatePaymentTxsAsync(swap, lockTimeInSeconds)
                .ConfigureAwait(false))
                .ToList();

            if (txs.Count == 0)
            {
                Log.Error("Can't create payment transactions");
                return;
            }

            try
            {
                foreach (int i in new int[] { 0, 1})
                {
                    var approvalTxs = txs
                        .Where((tx, j) => j % 3 == i)
                        .ToList();
                    
                    var isApproved = await ApproveAsync(swap, approvalTxs, cancellationToken)
                        .ConfigureAwait(false);

                    if (!isApproved)
                    {
                        Log.Error("Approve txs are not confirmed after timeout {@timeout}", InitiationTimeout.Minutes);
                        return;
                    }
                }

                txs = txs
                    .Where(tx => tx.Type.HasFlag(BlockchainTransactionType.SwapPayment))
                    .ToList();

                var isInitiateTx = true;

                foreach (var tx in txs)
                {
                    var signResult = await SignTransactionAsync(tx, cancellationToken)
                        .ConfigureAwait(false);

                    if (!signResult)
                    {
                        Log.Error("Transaction signing error");
                        return;
                    }

                    if (isInitiateTx)
                    {
                        swap.PaymentTx = tx;
                        swap.StateFlags |= SwapStateFlags.IsPaymentSigned;
                        RaiseSwapUpdated(swap, SwapStateFlags.IsPaymentSigned);
                    }

                    await BroadcastTxAsync(swap, tx, cancellationToken)
                        .ConfigureAwait(false);

                    if (isInitiateTx)
                    {
                        swap.PaymentTx = tx;
                        swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;
                        RaiseSwapUpdated(swap, SwapStateFlags.IsPaymentBroadcast);

                        isInitiateTx = false;

                        // delay for contract initiation
                        if (txs.Count > 1)
                        {
                            var isInitiated = await WaitPaymentConfirmationAsync(
                                    txId: tx.Id,
                                    timeout: InitiationTimeout,
                                    cancellationToken: cancellationToken)
                                .ConfigureAwait(false);

                            if (!isInitiated)
                            {
                                Log.Error("Initiation payment tx not confirmed after timeout {@timeout}", InitiationTimeout.Minutes);
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap payment error for swap {@swapId}", swap.Id);
                return;
            }

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
            {
                // start redeem control async
                await StartWaitForRedeemAsync(swap, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private async Task<bool> ApproveAsync(
            Swap swap,
            IEnumerable<EthereumTransaction> approvalTxs,
            CancellationToken cancellationToken = default,
            bool waitForConfirm = true)
        {
            approvalTxs = approvalTxs.Where(tx => tx != null && tx.Type.HasFlag(BlockchainTransactionType.TokenApprove));

            if (!approvalTxs.Any())
                return true;

            foreach (var approvalTx in approvalTxs)
            {
                var signResult = await SignTransactionAsync(approvalTx, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                {
                    Log.Error("Approve transaction signing error");
                    return false;
                }

                await BroadcastTxAsync(swap, approvalTx, cancellationToken, false, true, false)
                    .ConfigureAwait(false);
            }

            if (!waitForConfirm)
                return true;

            // wait confirmations for all approve txs
            var approveResults = await Task.WhenAll(
                approvalTxs.Select(atx => WaitPaymentConfirmationAsync(
                    txId: atx.Id,
                    timeout: InitiationTimeout,
                    cancellationToken: cancellationToken)))
                .ConfigureAwait(false);

            return !approveResults.Contains(false);
        }

        public override Task StartPartyPaymentControlAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            // initiator waits "accepted" event, acceptor waits "initiated" event
            var initiatedHandler = swap.IsInitiator
                ? new Action<Swap, CancellationToken>(SwapAcceptedHandler)
                : new Action<Swap, CancellationToken>(SwapInitiatedHandler);

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultAcceptorLockTimeInSeconds
                : DefaultInitiatorLockTimeInSeconds;

            var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds)).ToUnixTimeSeconds();

            ERC20SwapInitiatedHelper.StartSwapInitiatedControlAsync(
                    swap: swap,
                    currency: Erc20,
                    lockTimeInSec: lockTimeInSeconds,
                    interval: ConfirmationCheckInterval,
                    initiatedHandler: initiatedHandler,
                    canceledHandler: SwapCanceledHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        public override async Task RedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var erc20 = Erc20;

            var secretResult = await ERC20SwapRedeemedHelper
                .IsRedeemedAsync(
                    swap: swap,
                    currency: erc20,
                    attempts: MaxRedeemCheckAttempts,
                    attemptIntervalInSec: RedeemCheckAttemptIntervalInSec,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!secretResult.HasError && secretResult.Value != null)
            {
                RedeemConfirmedEventHandler(swap, null, cancellationToken);
                return;
            }

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast))
            {
                // redeem already broadcast
                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: erc20,
                        txId: swap.RedeemTx.Id,
                        confirmationHandler: RedeemConfirmedEventHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();

                return;
            }

            Log.Debug("Create redeem for swap {@swapId}", swap.Id);

            var walletAddress = (await Erc20Account
                .GetUnspentAddressesAsync(
                    toAddress: swap.ToAddress,
                    amount: 0,
                    fee: 0,
                    feePrice: 0,
                    feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                    addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress,
                    transactionType: BlockchainTransactionType.SwapRedeem,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .FirstOrDefault();

            if (walletAddress == null)
            {
                Log.Error("Insufficient funds for redeem");
                return;
            }

            var nonceResult = await EthereumNonceManager.Instance
                .GetNonceAsync(erc20, walletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            if (nonceResult.HasError)
            {
                Log.Error("Nonce getting error with code {@code} and description {@description}",
                    nonceResult.Error.Code,
                    nonceResult.Error.Description);

                return;
            }

            var message = new ERC20RedeemFunctionMessage
            {
                FromAddress = walletAddress.Address,
                HashedSecret = swap.SecretHash,
                Secret = swap.Secret,
                Nonce = nonceResult.Value,
                GasPrice = Atomex.Ethereum.GweiToWei(erc20.GasPriceInGwei),
            };

            message.Gas = await EstimateGasAsync(message, new BigInteger(erc20.RedeemGasLimit))
                .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(erc20.SwapContractAddress);

            var redeemTx = new EthereumTransaction(erc20, txInput)
            {
                Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRedeem
            };

            var signResult = await SignTransactionAsync(redeemTx, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
            {
                Log.Error("Transaction signing error");
                return;
            }

            swap.RedeemTx = redeemTx;
            swap.StateFlags |= SwapStateFlags.IsRedeemSigned;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemSigned);

            await BroadcastTxAsync(swap, redeemTx, cancellationToken)
                .ConfigureAwait(false);

            swap.RedeemTx = redeemTx;
            swap.StateFlags |= SwapStateFlags.IsRedeemBroadcast;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemBroadcast);

            TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: erc20,
                    txId: redeemTx.Id,
                    confirmationHandler: RedeemConfirmedEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        public override async Task RedeemForPartyAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Create redeem for counterParty for swap {@swapId}", swap.Id);

            var erc20 = Erc20;

            var walletAddress = (await Erc20Account
                .GetUnspentAddressesAsync(
                    toAddress: null, // todo: get participant address
                    amount: 0,
                    fee: 0,
                    feePrice: 0,
                    feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                    addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress,
                    transactionType: BlockchainTransactionType.SwapRedeem,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .FirstOrDefault();

            if (walletAddress == null)
            {
                Log.Error("Insufficient balance for party redeem. Cannot find the address containing the required amount of funds.");
                return;
            }

            var nonceResult = await EthereumNonceManager.Instance
                .GetNonceAsync(erc20, walletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            if (nonceResult.HasError)
            {
                Log.Error("Nonce getting error with code {@code} and description {@description}",
                    nonceResult.Error.Code,
                    nonceResult.Error.Description);

                return;
            }

            var message = new RedeemFunctionMessage
            {
                FromAddress = walletAddress.Address,
                HashedSecret = swap.SecretHash,
                Secret = swap.Secret,
                Nonce = nonceResult.Value,
                GasPrice = Atomex.Ethereum.GweiToWei(erc20.GasPriceInGwei),
            };

            message.Gas = await EstimateGasAsync(message, new BigInteger(erc20.RedeemGasLimit))
                .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(erc20.SwapContractAddress);

            var redeemTx = new EthereumTransaction(erc20, txInput)
            {
                Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRedeem
            };

            var signResult = await SignTransactionAsync(redeemTx, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
            {
                Log.Error("Transaction signing error");
                return;
            }

            await BroadcastTxAsync(swap, redeemTx, cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task RefundAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var erc20 = Erc20;

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast))
            {
                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: erc20,
                        txId: swap.RefundTx.Id,
                        confirmationHandler: RefundConfirmedEventHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();

                return;
            }

            Log.Debug("Create refund for swap {@swap}", swap.Id);

            var walletAddress = (await Erc20Account
                .GetUnspentAddressesAsync(
                    toAddress: null, // get refund address
                    amount: 0,
                    fee: 0,
                    feePrice: 0,
                    feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                    addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress,
                    transactionType: BlockchainTransactionType.SwapRefund,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .FirstOrDefault();

            if (walletAddress == null)
            {
                Log.Error("Insufficient funds for refund");
                return;
            }

            var nonceResult = await EthereumNonceManager.Instance
                .GetNonceAsync(erc20, walletAddress.Address)
                .ConfigureAwait(false);

            if (nonceResult.HasError)
            {
                Log.Error("Nonce getting error with code {@code} and description {@description}",
                    nonceResult.Error.Code,
                    nonceResult.Error.Description);

                return;
            }

            var message = new ERC20RefundFunctionMessage
            {
                FromAddress = walletAddress.Address,
                HashedSecret = swap.SecretHash,
                GasPrice = Atomex.Ethereum.GweiToWei(erc20.GasPriceInGwei),
                Nonce = nonceResult.Value,
            };

            message.Gas = await EstimateGasAsync(message, new BigInteger(erc20.RefundGasLimit))
                .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(erc20.SwapContractAddress);

            var refundTx = new EthereumTransaction(erc20, txInput)
            {
                Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRefund
            };

            var signResult = await SignTransactionAsync(refundTx, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
            {
                Log.Error("Transaction signing error");
                return;
            }

            swap.RefundTx = refundTx;
            swap.StateFlags |= SwapStateFlags.IsRefundSigned;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundSigned);

            await BroadcastTxAsync(swap, refundTx, cancellationToken)
                .ConfigureAwait(false);

            swap.RefundTx = refundTx;
            swap.StateFlags |= SwapStateFlags.IsRefundBroadcast;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundBroadcast);

            TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: erc20,
                    txId: refundTx.Id,
                    confirmationHandler: RefundConfirmedEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        public override Task StartWaitForRedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Wait redeem for swap {@swapId}", swap.Id);

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            // start redeem control async
            ERC20SwapRedeemedHelper.StartSwapRedeemedControlAsync(
                    swap: swap,
                    currency: Erc20,
                    refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds),
                    interval: TimeSpan.FromSeconds(30),
                    cancelOnlyIfRefundTimeReached: true,
                    redeemedHandler: RedeemCompletedEventHandler,
                    canceledHandler: RedeemCanceledEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        public override Task StartWaitForRedeemBySomeoneAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Wait redeem for swap {@swapId}", swap.Id);

            // start redeem control async
            ERC20SwapRedeemedHelper.StartSwapRedeemedControlAsync(
                    swap: swap,
                    currency: Erc20,
                    refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds),
                    interval: TimeSpan.FromSeconds(30),
                    cancelOnlyIfRefundTimeReached: true,
                    redeemedHandler: RedeemBySomeoneCompletedEventHandler,
                    canceledHandler: RedeemBySomeoneCanceledEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }
        
        #region Event Handlers
        
        private void SwapInitiatedHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug(
                "Initiator payment transaction received. Now counter party can broadcast payment tx for swap {@swapId}",
                swap.Id);

            swap.StateFlags |= SwapStateFlags.HasPartyPayment;
            swap.StateFlags |= SwapStateFlags.IsPartyPaymentConfirmed;
            RaiseSwapUpdated(swap, SwapStateFlags.HasPartyPayment | SwapStateFlags.IsPartyPaymentConfirmed);

            InitiatorPaymentConfirmed?.Invoke(this, new SwapEventArgs(swap));
        }

        private async void SwapAcceptedHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug(
                    "Acceptors payment transaction received. Now initiator can do self redeem and do party redeem for acceptor (if needs and wants) for swap {@swapId}.",
                    swap.Id);

                swap.StateFlags |= SwapStateFlags.HasPartyPayment;
                swap.StateFlags |= SwapStateFlags.IsPartyPaymentConfirmed;
                RaiseSwapUpdated(swap, SwapStateFlags.HasPartyPayment | SwapStateFlags.IsPartyPaymentConfirmed);

                RaiseAcceptorPaymentConfirmed(swap);

                await RedeemAsync(swap, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap accepted error");
            }
        }

        private void SwapCanceledHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            // todo: do smth here
            Log.Debug("Swap canceled due to wrong counter party params {@swapId}", swap.Id);
        }

        private void RedeemCompletedEventHandler(
            Swap swap,
            byte[] secret,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem control completed event for swap {@swapId}", swap.Id);

            if (swap.IsAcceptor)
            {
                swap.Secret = secret;
                RaiseSwapUpdated(swap, SwapStateFlags.HasSecret);

                RaiseAcceptorPaymentSpent(swap);
            }
        }

        private void RedeemCanceledEventHandler(
            Swap swap,
            DateTime refundTimeUtc,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem control canceled event for swap {@swapId}", swap.Id);

            ControlRefundTimeAsync(
                    swap: swap,
                    refundTimeUtc: refundTimeUtc,
                    refundTimeReachedHandler: RefundTimeReachedHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        private async void RefundTimeReachedHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Refund time reached for swap {@swapId}", swap.Id);

            try
            {
                var isRefundedResult = await ERC20SwapRefundedHelper.IsRefundedAsync(
                        swap: swap,
                        currency: Erc20,
                        attempts: MaxRefundCheckAttempts,
                        attemptIntervalInSec: RefundCheckAttemptIntervalInSec,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!isRefundedResult.HasError)
                {
                    if (isRefundedResult.Value)
                    {
                        RefundConfirmedEventHandler(swap, swap.RefundTx, cancellationToken);
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

        private void RefundConfirmedEventHandler(
            Swap swap,
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            swap.StateFlags |= SwapStateFlags.IsRefundConfirmed;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundConfirmed);
        }

        private void RedeemBySomeoneCompletedEventHandler(
            Swap swap,
            byte[] secret,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem party control completed event for swap {@swapId}", swap.Id);

            if (swap.IsAcceptor)
            {
                swap.Secret = secret;
                swap.StateFlags |= SwapStateFlags.IsRedeemConfirmed;
                RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemConfirmed);

                // get transactions & update balance for address async
                AddressHelper.UpdateAddressBalanceAsync<ERC20WalletScanner, ERC20Account, EthereumAccount>(
                        account: Erc20Account,
                        baseAccount: EthereumAccount,
                        address: swap.ToAddress,
                        cancellationToken: cancellationToken)
                    .FireAndForget();
            }
        }

        private async void RedeemBySomeoneCanceledEventHandler(
            Swap swap,
            DateTime refundTimeUtc,
            CancellationToken cancellationToken)
        {
            Log.Debug("Handle redeem party control canceled event for swap {@swapId}", swap.Id);

            try
            {
                if (swap.Secret?.Length > 0)
                {
                    var walletAddress = (await Erc20Account
                        .GetUnspentAddressesAsync(
                            toAddress: swap.ToAddress,
                            amount: 0,
                            fee: 0,
                            feePrice: 0,
                            feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                            addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress,
                            transactionType: BlockchainTransactionType.SwapRedeem,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false))
                        .FirstOrDefault();

                    if (walletAddress == null) //todo: make some panic here
                    {
                        Log.Error(
                            "Counter counterParty redeem need to be made for swap {@swapId}, using secret {@Secret}",
                            swap.Id,
                            Convert.ToBase64String(swap.Secret));
                        return;
                    }

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

        protected override async Task<IEnumerable<EthereumTransaction>> CreatePaymentTxsAsync(
            Swap swap,
            int lockTimeInSeconds,
            CancellationToken cancellationToken = default)
        {
            var erc20 = Erc20;

            Log.Debug("Create payment transactions for swap {@swapId}", swap.Id);

            var requiredAmountInERC20 = AmountHelper.QtyToAmount(swap.Side, swap.Qty, swap.Price, erc20.DigitsMultiplier);
            var refundTimeStampUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds)).ToUnixTimeSeconds();
            var isInitTx = true;
            var rewardForRedeemInERC20 = swap.PartyRewardForRedeem;

            var unspentAddresses = (await Erc20Account
                .GetUnspentAddressesAsync(cancellationToken)
                .ConfigureAwait(false))
                .ToList()
                .SortList((a, b) => a.AvailableBalance().CompareTo(b.AvailableBalance()));

            var transactions = new List<EthereumTransaction>();

            foreach (var walletAddress in unspentAddresses)
            {
                Log.Debug("Create swap payment tx from address {@address} for swap {@swapId}", walletAddress.Address, swap.Id);

                var balanceInEth = (await EthereumAccount
                    .GetAddressBalanceAsync(
                        address: walletAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
                    .Available;

                var balanceInERC20 = (await Erc20Account
                    .GetAddressBalanceAsync(
                        address: walletAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
                    .Available;

                Log.Debug("Available balance: {@balance}", balanceInERC20);

                var feeAmountInEth = (isInitTx
                    ? rewardForRedeemInERC20 == 0
                        ? erc20.InitiateFeeAmount
                        : erc20.InitiateWithRewardFeeAmount
                    : erc20.AddFeeAmount) + erc20.ApproveFeeAmount;

                if (balanceInEth - feeAmountInEth <= 0)
                {
                    Log.Warning(
                        "Insufficient funds at {@address}. Balance: {@balance}, feeAmount: {@feeAmount}, result: {@result}.",
                        walletAddress.Address,
                        balanceInEth,
                        feeAmountInEth,
                        balanceInEth - feeAmountInEth);

                    continue;
                }

                var amountInERC20 = requiredAmountInERC20 > 0
                    ? AmountHelper.DustProofMin(balanceInERC20, requiredAmountInERC20, erc20.DigitsMultiplier, erc20.DustDigitsMultiplier)
                    : 0;

                requiredAmountInERC20 -= amountInERC20;

                var nonceResult = await EthereumNonceManager.Instance
                    .GetNonceAsync(erc20, walletAddress.Address)
                    .ConfigureAwait(false);

                if (nonceResult.HasError)
                {
                    Log.Error("Nonce getting error with code {@code} and description {@description}",
                        nonceResult.Error.Code,
                        nonceResult.Error.Description);

                    return null;
                }

                var nonce = nonceResult.Value;

                var allowanceMessage = new ERC20AllowanceFunctionMessage()
                {
                    Owner = walletAddress.Address,
                    Spender = erc20.SwapContractAddress,
                    FromAddress = walletAddress.Address
                };

                var allowance = await ((IEthereumBlockchainApi) erc20.BlockchainApi)
                    .GetERC20AllowanceAsync(
                        erc20: erc20,
                        tokenAddress: erc20.ERC20ContractAddress,
                        allowanceMessage: allowanceMessage,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (allowance.Value > 0)
                {
                    transactions.Add(await CreateApproveTx(walletAddress, nonceResult.Value, 0)
                        .ConfigureAwait(false));
                    nonce += 1;
                }
                else
                {
                    transactions.Add(new EthereumTransaction());
                }

                transactions.Add(await CreateApproveTx(walletAddress, nonce, erc20.TokensToTokenDigits(amountInERC20))
                    .ConfigureAwait(false));
                nonce += 1;

                TransactionInput txInput;

                //actual transfer              
                if (isInitTx)
                {
                    var initMessage = new ERC20InitiateFunctionMessage
                    {
                        HashedSecret = swap.SecretHash,
                        ERC20Contract = erc20.ERC20ContractAddress,
                        Participant = swap.PartyAddress,
                        RefundTimestamp = refundTimeStampUtcInSec,
                        Countdown = lockTimeInSeconds,
                        Value = erc20.TokensToTokenDigits(amountInERC20),
                        RedeemFee = erc20.TokensToTokenDigits(rewardForRedeemInERC20),
                        Active = true,
                        FromAddress = walletAddress.Address,
                        GasPrice = Atomex.Ethereum.GweiToWei(erc20.GasPriceInGwei),
                        Nonce = nonce
                    };

                    var initiateGasLimit = rewardForRedeemInERC20 == 0
                        ? erc20.InitiateGasLimit
                        : erc20.InitiateWithRewardGasLimit;

                    initMessage.Gas = await EstimateGasAsync(initMessage, new BigInteger(initiateGasLimit))
                        .ConfigureAwait(false);

                    txInput = initMessage.CreateTransactionInput(erc20.SwapContractAddress);
                }
                else
                {
                    var addMessage = new ERC20AddFunctionMessage
                    {
                        HashedSecret = swap.SecretHash,
                        Value = erc20.TokensToTokenDigits(amountInERC20),
                        FromAddress = walletAddress.Address,
                        GasPrice = Atomex.Ethereum.GweiToWei(erc20.GasPriceInGwei),
                        Nonce = nonce
                    };

                    addMessage.Gas = await EstimateGasAsync(addMessage, new BigInteger(erc20.AddGasLimit))
                        .ConfigureAwait(false);

                    txInput = addMessage.CreateTransactionInput(erc20.SwapContractAddress);
                }

                transactions.Add(new EthereumTransaction(erc20, txInput)
                {
                    Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment
                });

                if (isInitTx)
                    isInitTx = false;

                if (requiredAmountInERC20 == 0)
                    break;
            }

            if (requiredAmountInERC20 > 0)
            {
                Log.Warning("Insufficient ERC20 or Eth funds (left {@requiredAmount}).", requiredAmountInERC20);
                return Enumerable.Empty<EthereumTransaction>();
            }

            return transactions;
        }

        private async Task<EthereumTransaction> CreateApproveTx(
            WalletAddress walletAddress,
            BigInteger nonce,
            BigInteger value)
        {
            var erc20 = Erc20;

            var message = new ERC20ApproveFunctionMessage
            {
                Spender = erc20.SwapContractAddress,
                Value = value,
                FromAddress = walletAddress.Address,
                GasPrice = Atomex.Ethereum.GweiToWei(erc20.GasPriceInGwei),
                Nonce = nonce,
            };

            message.Gas = await EstimateGasAsync(message, new BigInteger(erc20.ApproveGasLimit))
                        .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(erc20.ERC20ContractAddress);

            return new EthereumTransaction(erc20, txInput)
                {
                    Type = BlockchainTransactionType.Output | BlockchainTransactionType.TokenApprove
                };
        }

        private async Task<bool> SignTransactionAsync(
            EthereumTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await Erc20Account
                .GetAddressAsync(
                    address: tx.From,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return await Erc20Account.Wallet
                .SignAsync(
                    tx: tx,
                    address: walletAddress,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task BroadcastTxAsync(
            Swap swap,
            EthereumTransaction tx,
            CancellationToken cancellationToken = default,
            bool updateBalance = true,
            bool notifyIfUnconfirmed = true,
            bool notifyIfBalanceUpdated = true)
        {
            var broadcastResult = await Erc20.BlockchainApi
                .BroadcastAsync(tx, cancellationToken)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
                throw new InternalException(broadcastResult.Error);

            var txId = broadcastResult.Value;

            if (txId == null)
                throw new Exception("Transaction Id is null");

            Log.Debug("TxId {@id} for swap {@swapId}", txId, swap.Id);

            if (tx.Type.HasFlag(BlockchainTransactionType.SwapPayment))
                tx = tx.ParseERC20Input();

            // account new unconfirmed transaction
            await Erc20Account
                .UpsertTransactionAsync(
                    tx: tx,
                    updateBalance: updateBalance,
                    notifyIfUnconfirmed: notifyIfUnconfirmed,
                    notifyIfBalanceUpdated: notifyIfBalanceUpdated,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var ethTx = tx.Clone();
            ethTx.Currency = Eth;
            ethTx.Amount = 0;
            ethTx.Type = BlockchainTransactionType.Output | (ethTx.Type.HasFlag(BlockchainTransactionType.TokenApprove)
                ? BlockchainTransactionType.TokenCall
                : BlockchainTransactionType.SwapCall);

            await EthereumAccount
                .UpsertTransactionAsync(
                    tx: ethTx,
                    updateBalance: updateBalance,
                    notifyIfUnconfirmed: notifyIfUnconfirmed,
                    notifyIfBalanceUpdated: notifyIfBalanceUpdated,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // todo: transaction receipt status control
        }

        private async Task<BigInteger> EstimateGasAsync<TMessage>(
            TMessage message,
            BigInteger defaultGas) where TMessage : FunctionMessage, new()
        {
            try
            {
                var web3 = new Web3(Web3BlockchainApi.UriByChain(Eth.Chain));
                var txHandler = web3.Eth.GetContractTransactionHandler<TMessage>();

                var estimatedGas = await txHandler
                    .EstimateGasAsync(Erc20.SwapContractAddress, message)
                    .ConfigureAwait(false);

                Log.Debug("Estimated gas {@gas}", estimatedGas?.Value.ToString());

                return estimatedGas?.Value ?? defaultGas;
            }
            catch (Exception)
            {
                Log.Debug("Error while estimating fee");
            }

            return defaultGas;
        }

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

                var tx = await Eth.BlockchainApi
                    .GetTransactionAsync(txId, cancellationToken)
                    .ConfigureAwait(false);

                if (tx != null && !tx.HasError && tx.Value != null)
                {
                    if (tx.Value.State == BlockchainTransactionState.Confirmed)
                        return true;
                    if (tx.Value.State == BlockchainTransactionState.Failed)
                        return false;
                }
            }

            return false;
        }

        #endregion Helpers
    }
}
