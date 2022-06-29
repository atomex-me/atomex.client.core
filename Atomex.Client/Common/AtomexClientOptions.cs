using Microsoft.Extensions.Configuration;

namespace Atomex.Client.Common
{
    public record AtomexClientOptions
    {
        public bool CancelOrdersAfterConnect { get; init; }
        public bool CancelOrdersAfterDisconnect { get; init; }

        public static AtomexClientOptions DefaultOptions => new()
        {
            CancelOrdersAfterConnect = true,
            CancelOrdersAfterDisconnect = true,
        };

        public static AtomexClientOptions LoadFromConfiguration(IConfiguration configuration)
        {
            const string CancelOrdersAfterConnectPath = "CancelOrdersAfterConnect";
            const string CancelOrdersAfterDisconnectPath = "CancelOrdersAfterDisconnect";

            bool.TryParse(configuration[CancelOrdersAfterConnectPath], out var cancelOrdersAfterConnect);
            bool.TryParse(configuration[CancelOrdersAfterDisconnectPath], out var cancelOrdersAfterDisconnect);

            return new AtomexClientOptions
            {
                CancelOrdersAfterConnect = cancelOrdersAfterConnect,
                CancelOrdersAfterDisconnect = cancelOrdersAfterDisconnect,
            };
        }
    }
}