using Atomex.Wallets.Common;

namespace Atomex.Wallets.Tezos
{
    public class Counter : Parameter<int>
    {
        public bool UseOffline { get; private set; }
        public bool UseSync { get; private set; }

        public Counter(
            bool useNetwork,
            int? defaultValue,
            bool useOffline,
            bool useSync)
            : base(useNetwork, defaultValue)
        {
            UseOffline = useOffline;
            UseSync = useSync;
        }

        /// <summary>
        /// Use counter from passed value
        /// </summary>
        /// <param name="value">Counter value</param>
        /// <returns>Counter source</returns>
        public static Counter FromValue(int value) =>
            new(useNetwork: false,
                defaultValue: value,
                useOffline: false,
                useSync: false);

        /// <summary>
        /// Use counter from network, in case of failure use passed default value
        /// </summary>
        /// <param name="defaultValue">Counter default value</param>
        /// <param name="useOffline">If flag is set, offline counter is used in case when offline counter greater than network counter</param>
        /// <param name="useSync">Use counter synchronization by address</param>
        /// <returns>Counter source</returns>
        public static Counter FromNetwork(
            int defaultValue,
            bool useOffline = true,
            bool useSync = true) =>
            new(useNetwork: true,
                defaultValue: defaultValue,
                useOffline: useOffline,
                useSync: useSync);

        /// <summary>
        /// Use counter from network only
        /// </summary>
        /// <param name="useOffline">If flag is set, offline counter is used in case when offline counter greater than network counter</param>
        /// <param name="useSync">Use counter synchronization by address</param>
        /// <returns>Counter source</returns>
        public static Counter FromNetwork(
            bool useOffline = true,
            bool useSync = true) =>
            new(useNetwork: true,
                defaultValue: null,
                useOffline: useOffline,
                useSync: useSync);
    }
}