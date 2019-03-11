using Atomix.Core.Entities;

namespace Atomix
{
    public class EthBtc : Symbol
    {
        public EthBtc()
        {
            Id = 1;
            Name = $"{Currencies.Eth.Name}/{Currencies.Btc.Name}";
            Description = $"{Currencies.Eth.Description}/{Currencies.Btc.Description}";
            MinimumQty = 0.0001m; // todo: change minimum qty
            PriceDigits = Currencies.Eth.Digits;
            QtyDigits = Currencies.Ltc.Digits;
            Base = Currencies.Eth;
            Quote = Currencies.Btc;
        }

        public override decimal ToPrice(ulong price) =>
            price / (decimal) Bitcoin.BtcDigitsMultiplier;

        public override decimal ToQty(ulong qty) =>
            qty / (decimal) Ethereum.EthDigitsMultiplier;

        public override ulong PriceToUlong(decimal price) =>
            (ulong) (price * Bitcoin.BtcDigitsMultiplier);

        public override ulong QtyToUlong(decimal qty) =>
            (ulong) (qty * Ethereum.EthDigitsMultiplier);
    }
}