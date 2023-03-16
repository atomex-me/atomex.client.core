using System.Numerics;

namespace Atomex.Blockchain.Abstract
{
    public interface ITokenTransfer : ITransaction
    {
        public string Contract { get; }
        public BigInteger TokenId { get; }
    }
}