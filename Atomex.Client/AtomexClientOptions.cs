using Microsoft.Extensions.Configuration;

namespace Atomex.Client
{
    public record AtomexClientOptions
    {
        public bool CancelOrdersAfterConnect { get; init; }
        public bool CancelOrdersAfterDisconnect { get; init; }
        public bool StoreCanceledOrders { get; init; }

        public static AtomexClientOptions DefaultOptions => new()
        {
            CancelOrdersAfterConnect = true,
            CancelOrdersAfterDisconnect = true,
            StoreCanceledOrders = false
        };

        public static AtomexClientOptions LoadFromConfiguration(IConfiguration configuration)
        {
            const string CancelOrdersAfterConnectPath = "CancelOrdersAfterConnect";
            const string CancelOrdersAfterDisconnectPath = "CancelOrdersAfterDisconnect";
            const string StoreCanceledOrdersPath = "StoreCanceledOrders";

            bool.TryParse(configuration[CancelOrdersAfterConnectPath], out var cancelOrdersAfterConnect);
            bool.TryParse(configuration[CancelOrdersAfterDisconnectPath], out var cancelOrdersAfterDisconnect);
            bool.TryParse(configuration[StoreCanceledOrdersPath], out var storeCanceledOrders);

            return new AtomexClientOptions
            {
                CancelOrdersAfterConnect = cancelOrdersAfterConnect,
                CancelOrdersAfterDisconnect = cancelOrdersAfterDisconnect,
                StoreCanceledOrders = storeCanceledOrders
            };
        }
    }
}