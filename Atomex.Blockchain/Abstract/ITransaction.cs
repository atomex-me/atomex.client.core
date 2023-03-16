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

    public interface ITransaction
    {
        string Id { get; }
        string Currency { get; }
        TransactionStatus Status { get; set; }
        DateTimeOffset? CreationTime { get; }
        DateTimeOffset? BlockTime { get; }
        long BlockHeight { get; }
        long Confirmations { get; }
        bool IsConfirmed { get; }

        public void Fail() => Status = TransactionStatus.Failed;
    }
}