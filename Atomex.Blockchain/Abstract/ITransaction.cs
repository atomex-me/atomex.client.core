using System;

namespace Atomex.Blockchain.Abstract
{
    public enum TransactionStatus
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
        /// Transaction failed, removed or backtracked
        /// </summary>
        Failed
    }

    [Flags]
    public enum TransactionType
    {
        Unknown       = 0x00,
        Input         = 0x01,
        Output        = 0x02,
        SwapPayment   = 0x04,
        SwapRefund    = 0x08,
        SwapRedeem    = 0x10,
        TokenApprove  = 0x20,
        TokenTransfer = 0x40,
        ContractCall  = 0x80
    }

    public interface ITransaction
    {
        string Id { get; }
        string Currency { get; }
        TransactionStatus Status { get; }
        TransactionType Type { get; }
        DateTimeOffset? CreationTime { get; }
        DateTimeOffset? BlockTime { get; }
        long BlockHeight { get; }
        long Confirmations { get; }
        bool IsConfirmed { get; }
        bool IsTypeResolved { get; }
    }
}