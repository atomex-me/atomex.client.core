namespace Atomex.Wallet.Tezos
{
    public class Fee : Parameter<long>
    {
        protected Fee(bool useNetwork, long? defaultValue)
            : base(useNetwork, defaultValue)
        {
        }

        /// <summary>
        /// Use fee from passed value
        /// </summary>
        /// <param name="value">Fee value</param>
        /// <returns>Fee source</returns>
        public static Fee FromValue(long value) =>
            new(useNetwork: false,
                defaultValue: value);

        /// <summary>
        /// Use estimated value from network, in case of failure use passed default value
        /// </summary>
        /// <param name="defaultValue">Fee default value</param>
        /// <returns>Fee source</returns>
        public static Fee FromNetwork(long defaultValue) =>
            new(useNetwork: true,
                defaultValue: defaultValue);

        /// <summary>
        /// Use estimated value from network only
        /// </summary>
        /// <returns>Fee source</returns>
        public static Fee FromNetwork() =>
            new(useNetwork: true,
                defaultValue: null);
    }
}