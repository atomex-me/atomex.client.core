using System;
using System.Numerics;

namespace Atomex.Blockchain.Abstract
{
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

    public interface ITransactionMetadata
    {
        public string Id { get; }
        public string Currency { get; }
        public TransactionType Type { get; }
        public BigInteger Amount { get; }
    }
}