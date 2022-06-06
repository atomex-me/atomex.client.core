using System;

namespace Atomex.MarketData
{
    public class Quote
    {
        public string Symbol { get; set; }
        public bool IsToken { get; set; }
        public DateTime TimeStamp { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public decimal DailyChangePercent { get; set; }

        public override string ToString() =>
            $"{{Bid: {Bid}, Ask: {Ask}}}";

        public bool IsValidBid() =>
            Bid != 0;

        public bool IsValidAsk() =>
            Ask != 0 && Ask != decimal.MaxValue;

        public decimal GetMiddlePrice() => IsValidBid() && IsValidAsk()
            ? (Ask + Bid) / 2
            : IsValidBid()
                ? Bid
                : IsValidAsk()
                    ? Ask
                    : 0m;
    }
}