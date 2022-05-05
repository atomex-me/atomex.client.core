using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Tezos.Abstract;
using Atomex.Common;

namespace Atomex.Wallets.Tezos.Common
{
    public static class CounterExtensions
    {
        public static async Task<(int? counter, Error error)> GetCounterAsync(
            this Counter counter,
            string from,
            int numberOfCounters,
            ITezosApi api,
            CancellationToken cancellationToken = default)
        {
            Error error = null;

            if (counter.UseNetwork)
            {
                var (counterFromNetwork, counterError) = await api
                    .GetCounterAsync(from, cancellationToken)
                    .ConfigureAwait(false);

                if (counterFromNetwork != null && counterError == null)
                {
                    return (
                        counter: counter.UseOffline
                            ? TezosOfflineCounter.Instance.GetOfflineCounter(
                                address: from,
                                counterFromNetwork: counterFromNetwork.Value,
                                numberOfCounters: numberOfCounters)
                            : counterFromNetwork,
                        error: null
                    );
                }

                error = counterError ?? (counterFromNetwork == null
                    ? new Error(Errors.GetCounterError, "Counter is null")
                    : null);
            }

            if (counter.UseValue)
                return (counter: counter.Value, error: null);

            return (counter: null, error);
        }
    }
}