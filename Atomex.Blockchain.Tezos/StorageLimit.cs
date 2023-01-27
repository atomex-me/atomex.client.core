namespace Atomex.Blockchain.Tezos
{
    public class StorageLimit : Parameter<int>
    {
        public bool UseSafeValue { get; private set; }

        private StorageLimit(
            bool useNetwork,
            int? defaultValue,
            bool useSafeValue)
            : base(useNetwork, defaultValue)
        {
            UseSafeValue = useSafeValue;
        }

        /// <summary>
        /// Use storage limit from passed value
        /// </summary>
        /// <param name="value">Storage limit value</param>
        /// <returns>Storage limit source</returns>
        public static StorageLimit FromValue(int value) =>
            new(useNetwork: false,
                defaultValue: value,
                useSafeValue: false);

        /// <summary>
        /// Use estimated value from network, in case of failure use passed default value
        /// </summary>
        /// <param name="defaultValue">Storage limit value</param>
        /// <param name="useSafeValue">If flag is set, the maximum value between the Network value and Default value is used</param>
        /// <returns>Storage limit source</returns>
        public static StorageLimit FromNetwork(int defaultValue, bool useSafeValue = true) =>
            new(useNetwork: true,
                defaultValue: defaultValue,
                useSafeValue: useSafeValue);

        /// <summary>
        /// Use estimated value from network only
        /// </summary>
        /// <returns>Storage limit source</returns>
        public static StorageLimit FromNetwork() =>
            new(useNetwork: true,
                defaultValue: null,
                useSafeValue: false);
    }
}