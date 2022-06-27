using System;
using System.Collections.Generic;

using Atomex.Client.V1.Entities;
using Atomex.Common;

#nullable enable

namespace Atomex.Client.Rest
{
    public partial class RestAtomexClient
    {
        private record OrderDto(
            long Id,
            string ClientOrderId,
            string Symbol,
            Side Side,
            DateTime TimeStamp,
            decimal Price,
            decimal Qty,
            decimal LeaveQty,
            OrderType Type,
            OrderStatus Status
        )
        {
            public List<TradeDto>? Trades { get; set; }
            public List<SwapDto>? Swaps { get; set; }
        }

        /// <param name="ClientOrderId">Client order identifier</param>
        /// <param name="Symbol">Symbol (e.g. `ETH/BTC`)</param>
        /// <param name="Price">Price</param>
        /// <param name="Qty">Qty</param>
        /// <param name="Side">Side</param>
        /// <param name="Type">Type</param>
        /// <param name="Requisites">Swap requisites</param>
        private record NewOrderDto(
            string ClientOrderId,
            string Symbol,
            decimal Price,
            decimal Qty,
            Side Side,
            OrderType Type,
            RequisitesDto Requisites
        )
        {
            /// <summary>
            /// Proofs that the client has funds to place order
            /// </summary>
            public List<ProofOfFundsDto>? ProofsOfFunds { get; set; }
        }

        private record ProofOfFundsDto(
            string Address,
            string Currency,
            long TimeStamp,
            string Message,
            string PublicKey,
            string Signature,
            string Algorithm
        );

        /// <param name="BaseCurrencyContract">Base currency contract address (`null` for Bitcoin based currencies)</param>
        /// <param name="QuoteCurrencyContract">Quote currency contract address (`null` for Bitcoin based currencies)</param>
        private record RequisitesDto(
            string? BaseCurrencyContract,
            string? QuoteCurrencyContract
        )
        {
            /// <summary>
            /// Secret hash
            /// </summary>
            public string? SecretHash { get; set; }
            /// <summary>
            /// Receiving address
            /// </summary>
            public string? ReceivingAddress { get; set; }
            /// <summary>
            /// Refund address
            /// </summary>
            public string? RefundAddress { get; set; }
            /// <summary>
            /// Reward for redeem
            /// </summary>
            public decimal RewardForRedeem { get; set; }
            /// <summary>
            /// Lock time
            /// </summary>
            public ulong LockTime { get; set; }
        }

        private record NewOrderResponseDto(long OrderId);

        private record OrdersCancelatonDto(int Count);

        private record OrderCancelationDto(bool Result);

        /// <param name="Id">Unique identifier</param>
        /// <param name="Symbol">Symbol (e.g. ETH/BTC)</param>
        /// <param name="Side">Side</param>
        /// <param name="TimeStamp">Time Stamp</param>
        /// <param name="Price">Price</param>
        /// <param name="Qty">Qty</param>
        /// <param name="IsInitiator">Is user swap initiator</param>
        private record SwapDto(
            long Id,
            string Symbol,
            Side Side,
            DateTime TimeStamp,
            decimal Price,
            decimal Qty,
            bool IsInitiator
        )
        {
            /// <summary>
            /// Swap secret
            /// </summary>
            public string? Secret { get; set; }

            /// <summary>
            /// Swap secret hash
            /// </summary>
            public string? SecretHash { get; set; }

            /// <summary>
            /// User swap information
            /// </summary>
            public SwapPartyDto? User { get; set; }

            /// <summary>
            /// Counterparty swap information
            /// </summary>
            public SwapPartyDto? CounterParty { get; set; }
        }

        private record InitiateSwapDto(
            string ReceivingAddress,
            decimal RewardForRedeem,
            ulong LockTime
        )
        {
            public string? SecretHash { get; set; }
            public string? RefundAddress { get; set; }
        }

        private record InitiateSwapResponseDto(bool Result);

        private enum PartyStatus
        {
            /// <summary>
            /// Swap created but details and payments have not yet sent
            /// </summary>
            Created,
            /// <summary>
            /// Requisites sent
            /// </summary>
            Involved,
            /// <summary>
            /// Own funds partially sent
            /// </summary>
            PartiallyInitiated,
            /// <summary>
            /// Own funds completely sent
            /// </summary>
            Initiated,
            /// <summary>
            /// Counterparty funds are already redeemed
            /// </summary>
            Redeemed,
            /// <summary>
            /// Own funds are already refunded
            /// </summary>
            Refunded,
            /// <summary>
            /// Own funds lost
            /// </summary>
            Lost,
            /// <summary>
            /// Own funds are already refunded and counterparty funds are already redeemed
            /// </summary>
            Jackpot
        }

        /// <param name="Status"></param>
        private record SwapPartyDto(
            PartyStatus Status
        )
        {
            /// <summary>
            /// Requisites
            /// </summary>
            public RequisitesDto? Requisites { get; set; }
            /// <summary>
            /// Trades
            /// </summary>
            public List<TradeDto>? Trades { get; set; }
            /// <summary>
            /// Transactions
            /// </summary>
            public List<PartyTransactionDto>? Transactions { get; set; }
        }

        private record TradeDto(
            long OrderId,
            decimal Price,
            decimal Qty
        );

        private enum TransactionStatus
        {
            /// <summary>
            /// Transaction in mempool
            /// </summary>
            Pending,
            /// <summary>
            /// Transaction has at least one confirmation
            /// </summary>
            Confirmed,
            /// <summary>
            /// Transaction canceled, removed or backtracked
            /// </summary>
            Canceled
        }

        private enum PartyTransactionType
        {
            Lock,
            AdditionalLock,
            Redeem,
            Refund
        }

        private record PartyTransactionDto(
            string Currency,
            string TxId,
            long BlockHeight,
            long Confirmations,
            TransactionStatus Status,
            PartyTransactionType Type
        );
    }
}