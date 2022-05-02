namespace Atomex.Wallet
{
    public class Balance_OLD
    {
        public decimal Confirmed { get; }
        public decimal UnconfirmedIncome { get; }
        public decimal UnconfirmedOutcome { get; }

        public decimal Available => Confirmed + UnconfirmedOutcome;

        public Balance_OLD()
        { 
        }

        public Balance_OLD(
            decimal confirmed,
            decimal unconfirmedIncome,
            decimal unconfirmedOutcome)
        {
            Confirmed = confirmed;
            UnconfirmedIncome = unconfirmedIncome;
            UnconfirmedOutcome = unconfirmedOutcome;
        }
    }
}