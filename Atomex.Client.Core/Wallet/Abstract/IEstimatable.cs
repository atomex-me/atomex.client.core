using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;

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

    public interface IEstimatable
    {
        Task<(decimal amount, decimal fee, decimal reserved)> EstimateMaxAmountToSendAsync(
            IFromSource from,
            string to,
            BlockchainTransactionType type,
            decimal fee = 0,
            decimal feePrice = 0,
            bool reserve = false,
            CancellationToken cancellationToken = default);

        Task<decimal?> EstimateFeeAsync(
            IFromSource from,
            string to,
            decimal amount,
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default);
    }
}