namespace Atomex.MarketData
{
    public enum TimeFrame
    {
        M1,
        M5,
        M15,
        M30,
        H1,
        H3,
        H6,
        H12,
        D1,
        W1
    }

    public class Candle
    {
        public long TimeStamp;
        public decimal Open;
        public decimal High;
        public decimal Low;
        public decimal Close;
        public decimal Volume;
    }
}