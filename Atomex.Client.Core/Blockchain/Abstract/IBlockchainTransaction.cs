using System;
using Atomex.Core;

namespace Atomex.Blockchain.Abstract
{
    public enum BlockchainTransactionState
    {
        Unknown,
        Pending,
        Unconfirmed,
        Confirmed,
        Failed
    }

    [Flags]
    public enum BlockchainTransactionType
    {
        Unknown = 0x00,
        Input = 0x01,
        Output = 0x02,
        SwapPayment = 0x04,
        SwapRefund = 0x08,
        SwapRedeem = 0x10,
        TokenApprove = 0x20
    }

    public interface IBlockchainTransaction
    {
        string Id { get; }
        Currency Currency { get; }
        BlockInfo BlockInfo { get; }
        BlockchainTransactionState State { get; set; }
        BlockchainTransactionType Type { get; set; }
        DateTime? CreationTime { get; set; }

        bool IsConfirmed { get; }
    }
}