using Atomex.Common;

namespace Atomex.Blockchain
{
    public class TokenBalance : Token
    {
        public string Address { get; set; }
        public string Balance { get; set; } = "0";
        public decimal? ParsedBalance { get; set; }
        public int TransfersCount { get; set; }

        public decimal GetTokenBalance() => ParsedBalance ??=
            Balance.TryParseWithRound(Decimals, out var result)
                ? result
                : 0;
    }
}