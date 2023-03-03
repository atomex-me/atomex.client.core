using System.Numerics;

namespace Atomex.Abstract
{
    public interface ITokenConfig
    {
        public string TokenContractAddress { get; }
        public BigInteger TokenId { get; }
    }
}