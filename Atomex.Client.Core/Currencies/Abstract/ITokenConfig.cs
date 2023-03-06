using System.Numerics;

namespace Atomex.Abstract
{
    public interface ITokenConfig
    {
        public string Standard { get; }
        public string TokenContractAddress { get; }
        public BigInteger TokenId { get; }
        public string BaseCurrencyName { get; }
    }
}