using Atomex.Wallets.Common;

namespace Atomex.Wallets.Ethereum
{
    public class Nonce : Parameter<long>
    {
        public bool UsePending { get; private set; }
        public bool UseOffline { get; private set; }
        public bool UseSync { get; private set; }

        protected Nonce(
            bool useNetwork,
            long? defaultValue,
            bool usePending,
            bool useOffline,
            bool useSync)
            : base(useNetwork, defaultValue)
        {
            UsePending = usePending;
            UseOffline = useOffline;
            UseSync = useSync;
        }

        /// <summary>
        /// Use nonce from passed value
        /// </summary>
        /// <param name="value">Nonce value</param>
        /// <returns>Nonce source</returns>
        public static Nonce FromValue(long value) =>
            new(useNetwork: false,
                defaultValue: value,
                usePending: false,
                useOffline: false,
                useSync: false);

        /// <summary>
        /// Use nonce from network, in case of failure use passed default value
        /// </summary>
        /// <param name="defaultValue">Nonce default value</param>
        /// <param name="usePending">Use nonce value considering Pending transactions</param>
        /// <param name="useOffline">Use offline nonce value if it is greater than the network value</param>
        /// <param name="useSync">Use nonce synchronization by address</param>
        /// <returns>Nonce source</returns>
        public static Nonce FromNetwork(
            long defaultValue,
            bool usePending = true,
            bool useOffline = true,
            bool useSync = true) =>
            new(useNetwork: true,
                defaultValue: defaultValue,
                usePending: usePending,
                useOffline: useOffline,
                useSync: useSync);

        /// <summary>
        /// Use nonce from network only
        /// </summary>
        /// <param name="usePending">Use nonce value considering Pending transactions</param>
        /// <param name="useOffline">Use offline nonce value if it is greater than the network value</param>
        /// <param name="useSync">Use nonce synchronization by address</param>
        /// <returns>Nonce source</returns>
        public static Nonce FromNetwork(
            bool usePending = true,
            bool useOffline = true,
            bool useSync = true) =>
            new(useNetwork: true,
                defaultValue: null,
                usePending: usePending,
                useOffline: useOffline,
                useSync: useSync);
    }
}