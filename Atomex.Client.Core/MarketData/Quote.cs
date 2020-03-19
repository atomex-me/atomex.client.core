using System;

namespace Atomex.MarketData
{
    public class Quote
    {
        public string Symbol { get; set; }
        public DateTime TimeStamp { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }

        public override string ToString()
        {
            return $"{{Bid: {Bid}, Ask: {Ask}}}";
        }

        public bool IsValid()
        {
            return Bid != 0 && Ask != 0 && Ask != decimal.MaxValue;
        }
    }
}