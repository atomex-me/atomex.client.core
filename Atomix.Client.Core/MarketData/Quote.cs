using System;

namespace Atomix.MarketData
{
    public class Quote
    {
        public int SymbolId { get; set; }
        public DateTime TimeStamp { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }

        public override string ToString()
        {
            return $"{{Bid: {Bid}, Ask: {Ask}}}";
        }
    }
}