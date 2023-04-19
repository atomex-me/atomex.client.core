using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Bitcoin;
using Atomex.Common;

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
        public IEnumerable<BitcoinTxOutput> Outputs { get; }

        public FromOutputs(IEnumerable<BitcoinTxOutput> outputs)
        {
            Outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
        }
    }

    public class MaxAmountEstimation
    {
        public BigInteger Amount { get; set; }
        public BigInteger Fee { get; set; }
        public BigInteger Reserved { get; set; }
        public Error? Error { get; set; }
        public string ErrorHint { get; set; }
    }

    public interface IEstimatable
    {
        Task<MaxAmountEstimation> EstimateMaxSwapPaymentAmountAsync(
            IFromSource fromSource,
            bool reserve = false,
            CancellationToken cancellationToken = default);

        Task<Result<decimal>> EstimateSwapPaymentFeeAsync(
            IFromSource from,
            decimal amount,
            CancellationToken cancellationToken = default);
    }
}