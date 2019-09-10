namespace Atomex.MarketData
{
    public enum SubscriptionType
    {
        TopOfBook,
        DepthTwenty,
        OrderLog
    }

    public struct Subscription
    {
        //public string Symbol { get; set; }
        public SubscriptionType Type { get; set; }
    }
}