namespace Atomex.Wallets.Common
{
    public class Parameter<T> where T : struct
    {
        private readonly T? _value;

        public T Value => _value.GetValueOrDefault();
        public bool UseNetwork { get; private set; }
        public bool UseValue => _value.HasValue;

        protected Parameter(bool useNetwork, T? defaultValue)
        {
            UseNetwork = useNetwork;
            _value     = defaultValue;
        }
    }
}