namespace Atomix.Wallet
{
    public class Balance
    {
        public decimal Confirmed { get; }
        public decimal UnconfirmedIncome { get; }
        public decimal UnconfirmedOutcome { get; }

        public decimal Available => Confirmed + UnconfirmedOutcome;

        public Balance()
        { 
        }

        public Balance(
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