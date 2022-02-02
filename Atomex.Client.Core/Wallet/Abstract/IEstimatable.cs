using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.BitcoinBased;
using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface IFromSource
    {
    }

    public class FromAddress : IFromSource
    {
        public string Address { get; }
        
        public FromAddress(string address)
        {
            Address = address ?? throw new ArgumentNullException(nameof(address));
        }
    }

    public class FromOutputs : IFromSource
    {
        public IEnumerable<BitcoinBasedTxOutput> Outputs { get; }

        public FromOutputs(IEnumerable<BitcoinBasedTxOutput> outputs)
        {
            Outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
        }
    }

    public class MaxAmountEstimation
    {
        public decimal Amount { get; set; }
        public decimal Fee { get; set; }
        public decimal Reserved { get; set; }
        public Error Error { get; set; }
    }

    public interface IEstimatable
    {
        Task<MaxAmountEstimation> EstimateMaxSwapPaymentAmountAsync(
            IFromSource from,
            bool reserve = false,
            CancellationToken cancellationToken = default);

        Task<decimal?> EstimateSwapPaymentFeeAsync(
            IFromSource from,
            decimal amount,
            CancellationToken cancellationToken = default);
    }
}