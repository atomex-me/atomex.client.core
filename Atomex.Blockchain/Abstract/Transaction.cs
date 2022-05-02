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
        Canceled
    }

    public class Transaction
    {
        public virtual string Id => TxId;
        public virtual string TxId { get; set; }
        public virtual string Currency { get; set; }
        public virtual int WalletId { get; set; }
        public virtual TransactionStatus Status { get; set; }
        public virtual DateTimeOffset? CreationTime { get; set; }
        public virtual DateTimeOffset? BlockTime { get; set; }
        public virtual long BlockHeight { get; set; }
        public virtual long Confirmations { get; set; }
    }
}