namespace Atomex.Blockchain.Tezos
{
    public class GasLimit : Parameter<int>
    {
        protected GasLimit(bool useNetwork, int? defaultValue)
            : base(useNetwork, defaultValue)
        {
        }

        /// <summary>
        /// Use gas limit from passed value
        /// </summary>
        /// <param name="value">Gas limit value</param>
        /// <returns>Gas limit source</returns>
        public static GasLimit FromValue(int value) =>
            new(useNetwork: false,
                defaultValue: value);

        /// <summary>
        /// Use estimated value from network, in case of failure use passed value
        /// </summary>
        /// <param name="defaultValue">Gas limit default value</param>
        /// <returns>Gas limit source</returns>
        public static GasLimit FromNetwork(int defaultValue) =>
            new(useNetwork: true,
                defaultValue: defaultValue);

        /// <summary>
        /// Use estimated value from network only
        /// </summary>
        /// <returns>Gas limit source</returns>
        public static GasLimit FromNetwork() =>
            new(useNetwork: true,
                defaultValue: null);
    }
}