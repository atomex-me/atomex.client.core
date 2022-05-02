using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Core;
using Atomex.Wallet.Abstract;

namespace Atomex.Blockchain.Abstract
{
    public interface IInOutTransaction_OLD : IBlockchainTransaction_OLD
    {
        ITxPoint[] Inputs { get; }
        ITxOutput[] Outputs { get; }
        long? Fees { get; set; }
        long Amount { get; set; }

        Task<bool> SignAsync(
            IAddressResolver addressResolver,
            IKeyStorage_OLD keyStorage,
            IEnumerable<ITxOutput> spentOutputs,
            CurrencyConfig currencyConfig,
            CancellationToken cancellationToken = default);
    }
}