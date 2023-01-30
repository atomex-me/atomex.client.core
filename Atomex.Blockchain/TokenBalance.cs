using System.Numerics;

namespace Atomex.Blockchain
{
    public class TokenBalance : Token
    {
        public string Address { get; set; }
        public string Balance { get; set; } = "0";
        public BigInteger? ParsedBalance { get; set; }
        public int TransfersCount { get; set; }

        public BigInteger GetTokenBalance() => ParsedBalance ??= BigInteger.TryParse(Balance, out var result)
            ? result
            : 0;
    }
}