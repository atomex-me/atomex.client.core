using Atomex.Wallets.Common;

namespace Atomex.Wallets.Ethereum
{
    public class GasPrice : Parameter<decimal>
    {
        protected GasPrice(bool useNetwork, decimal? defaultValue)
            : base(useNetwork, defaultValue)
        {
        }

        /// <summary>
        /// Use gas price from passed value
        /// </summary>
        /// <param name="value">Gas price value</param>
        /// <returns>Gas price source</returns>
        public static GasPrice FromValue(decimal value) =>
            new(useNetwork: false, defaultValue: value);

        /// <summary>
        /// Use value from network, in case of failure use passed default value
        /// </summary>
        /// <param name="defaultValue">Gas price default value</param>
        /// <returns>Gas price source</returns>
        public static GasPrice FromNetwork(decimal defaultValue) =>
            new(useNetwork: true, defaultValue: defaultValue);

        /// <summary>
        /// Use value from network only
        /// </summary>
        /// <returns>Gas price source</returns>
        public static GasPrice FromNetwork() =>
            new(useNetwork: true, defaultValue: null);
    }
}