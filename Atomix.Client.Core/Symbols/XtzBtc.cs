using Atomix.Core.Entities;

namespace Atomix
{
    public class XtzBtc : Symbol
    {
        public XtzBtc()
        {
            Id = 2;
            Name = $"{Currencies.Xtz.Name}/{Currencies.Btc.Name}";
            Description = $"{Currencies.Xtz.Description}/{Currencies.Btc.Description}";
            MinimumQty = 0.0001m; // todo: change minimum qty
            PriceDigits = Currencies.Btc.Digits;
            QtyDigits = Currencies.Xtz.Digits;
            Base = Currencies.Xtz;
            Quote = Currencies.Btc;
        }

        public override decimal ToPrice(ulong price) =>
            price / (decimal)Bitcoin.BtcDigitsMultiplier;

        public override decimal ToQty(ulong qty) =>
            qty / (decimal)Tezos.XtzDigitsMultiplier;

        public override ulong PriceToUlong(decimal price) =>
            (ulong)(price * Bitcoin.BtcDigitsMultiplier);

        public override ulong QtyToUlong(decimal qty) =>
            (ulong)(qty * Tezos.XtzDigitsMultiplier);
    }
}