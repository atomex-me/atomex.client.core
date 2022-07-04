namespace Atomex.Client.V1.Entities
{
    public enum SubscriptionType
    {
        TopOfBook,
        DepthTwenty,
        OrderLog
    }

    public struct Subscription
    {
        public SubscriptionType Type { get; set; }
    }
}