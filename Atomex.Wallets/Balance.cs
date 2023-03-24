using System.Numerics;

namespace Atomex.Wallets
{
    public class Balance
    {
        public BigInteger Confirmed { get; set; }
        public BigInteger UnconfirmedIncome { get; set; }
        public BigInteger UnconfirmedOutcome { get; set; }

        public Balance()
        {
        }

        public Balance(
            BigInteger confirmed,
            BigInteger unconfirmedIncome,
            BigInteger unconfirmedOutcome)
        {
            Confirmed = confirmed;
            UnconfirmedIncome = unconfirmedIncome;
            UnconfirmedOutcome = unconfirmedOutcome;
        }
    }
}