namespace Atomex.Wallet
{
    public class Balance
    {
        public decimal Confirmed { get; set; }
        public decimal UnconfirmedIncome { get; set; }
        public decimal UnconfirmedOutcome { get; set; }

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