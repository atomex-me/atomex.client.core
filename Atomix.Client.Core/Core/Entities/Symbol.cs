namespace Atomix.Core.Entities
{
    public abstract class Symbol
    {
        public const int MaxNameLength = 32;

        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal MinimumQty { get; set; }
        public int PriceDigits { get; set; }
        public int QtyDigits { get; set; }
        public int BaseId { get; set; }
        public Currency Base { get; set; }
        public int QuoteId { get; set; }
        public Currency Quote { get; set; }

        public abstract decimal ToPrice(ulong price);
        public abstract decimal ToQty(ulong qty);
        public abstract ulong PriceToUlong(decimal price);
        public abstract ulong QtyToUlong(decimal qty);
    }
}