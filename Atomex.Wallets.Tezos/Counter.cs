using Atomex.Wallets.Common;

namespace Atomex.Wallets.Tezos
{
    public class Counter : Parameter<int>
    {
        protected Counter(
            bool useNetwork,
            int? defaultValue)
            : base(useNetwork, defaultValue)
        {
        }

        /// <summary>
        /// Use counter from passed value
        /// </summary>
        /// <param name="value">Counter value</param>
        /// <returns>Counter source</returns>
        public static Counter FromValue(int value) =>
            new(useNetwork: false, defaultValue: value);

        /// <summary>
        /// Use counter from network, in case of failure use passed default value
        /// </summary>
        /// <param name="defaultValue">Counter default value</param>
        /// <returns>Counter source</returns>
        public static Counter FromNetwork(int defaultValue) =>
            new(useNetwork: true, defaultValue: defaultValue);

        /// <summary>
        /// Use counter from network only
        /// </summary>
        /// <returns>Counter source</returns>
        public static Counter FromNetwork() =>
            new(useNetwork: true, defaultValue: null);
    }
}