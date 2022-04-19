using System.ComponentModel;

namespace Atomex.TzktEvents.Models
{
    /***
     * Enumerates subscription methods for TzKT Events (https://api.tzkt.io/#tag/Subscriptions)
     * and stores names of channels for each method in <see cref="Description"/> attribute.
     */
    public class SubscriptionMethod
    {
        public string Method { get; }
        public string Channel { get; }

        public SubscriptionMethod(string method, string channel)
        {
            Method = method;
            Channel = channel;
        }

        public static readonly SubscriptionMethod SubscribeToHead = new("SubscribeToHead", "head");
        public static readonly SubscriptionMethod SubscribeToBlocks = new("SubscribeToBlocks", "blocks");
        public static readonly SubscriptionMethod SubscribeToAccounts = new("SubscribeToAccount", "accounts");
        public static readonly SubscriptionMethod SubscribeToOperations = new("SubscribeToOperations", "operations");
        public static readonly SubscriptionMethod SubscribeToBigMaps = new("SubscribeToBigMaps", "bigmaps");
        public static readonly SubscriptionMethod SubscribeToTokenTransfers = new("SubscribeToTokenTransfers", "transfers");
    }
}
