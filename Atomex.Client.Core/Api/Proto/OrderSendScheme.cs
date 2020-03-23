using Atomex.Common.Proto;
using Atomex.Core;

namespace Atomex.Api.Proto
{
    public class OrderSendScheme : ProtoScheme<Order>
    {
        public OrderSendScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(WalletAddress), true)
                .AddRequired(nameof(WalletAddress.Address))
                .AddRequired(nameof(WalletAddress.Currency))
                .AddRequired(nameof(WalletAddress.PublicKey))
                .AddRequired(nameof(WalletAddress.ProofOfPossession))
                .AddRequired(nameof(WalletAddress.Nonce));

            Model.Add(typeof(Order), true)
                .AddRequired(nameof(Order.ClientOrderId))
                .AddRequired(nameof(Order.Symbol))
                .AddRequired(nameof(Order.TimeStamp))
                .AddRequired(nameof(Order.Price))
                .AddRequired(nameof(Order.Qty))
                .AddRequired(nameof(Order.Side))
                .AddRequired(nameof(Order.Type))
                .AddRequired(nameof(Order.FromWallets));
        }
    }
}