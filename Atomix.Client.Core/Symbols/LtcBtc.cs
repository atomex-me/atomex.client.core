using Atomix.Core.Entities;

namespace Atomix
{
    public class LtcBtc : Symbol
    {
        public LtcBtc()
        {
            Id = 0;
            Name = $"{Currencies.Ltc.Name}/{Currencies.Btc.Name}";
            Description = $"{Currencies.Ltc.Description}/{Currencies.Btc.Description}";
            MinimumQty = 0.0001m;
            PriceDigits = Currencies.Btc.Digits;
            QtyDigits = Currencies.Ltc.Digits;
            Base = Currencies.Ltc;
            Quote = Currencies.Btc;
        }

        public override decimal ToPrice(ulong price) =>
            price / (decimal) Bitcoin.BtcDigitsMultiplier;

        public override decimal ToQty(ulong qty) =>
            qty / (decimal) Litecoin.LtcDigitsMultiplier;

        public override ulong PriceToUlong(decimal price) =>
            (ulong) (price * Bitcoin.BtcDigitsMultiplier);

        public override ulong QtyToUlong(decimal qty) =>
            (ulong) (qty * Litecoin.LtcDigitsMultiplier);
    }
}