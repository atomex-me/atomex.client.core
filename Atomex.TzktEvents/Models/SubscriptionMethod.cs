using System.ComponentModel;

namespace Atomex.TzktEvents.Models
{
    /***
     * Enumerates subscription methods for TzKT Events (https://api.tzkt.io/#tag/Subscriptions)
     * and stores names of channels for each method in <see cref="Description"/> attribute.
     */
    public enum SubscriptionMethod
    {
        [Description("head")]
        SubscribeToHead,

        [Description("blocks")]
        SubscribeToBlocks,

        [Description("accounts")]
        SubscribeToAccount,

        [Description("operations")]
        SubscribeToOperations,

        [Description("bigmaps")]
        SubscribeToBigMaps,

        [Description("transfers")]
        SubscribeToTokenTransfers,
    }
}
