using Atomix.Core.Entities;

namespace Atomix
{
    public class XtzEth : Symbol
    {
        public XtzEth()
        {
            Id = 3;
            Name = $"{Currencies.Xtz.Name}/{Currencies.Eth.Name}";
            Description = $"{Currencies.Xtz.Description}/{Currencies.Eth.Description}";
            MinimumQty = 0.0001m; // todo: change minimum qty
            PriceDigits = Currencies.Eth.Digits;
            QtyDigits = Currencies.Xtz.Digits;
            Base = Currencies.Xtz;
            Quote = Currencies.Eth;
        }

        public override decimal ToPrice(ulong price) =>
            price / (decimal)Ethereum.EthDigitsMultiplier;

        public override decimal ToQty(ulong qty) =>
            qty / (decimal)Tezos.XtzDigitsMultiplier;

        public override ulong PriceToUlong(decimal price) =>
            (ulong)(price * Ethereum.EthDigitsMultiplier);

        public override ulong QtyToUlong(decimal qty) =>
            (ulong)(qty * Tezos.XtzDigitsMultiplier);
    }
}