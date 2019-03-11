using Atomix.Common.Proto;
using Atomix.Core.Entities;

namespace Atomix.Api.Proto
{
    public class OrderSendScheme : ProtoScheme
    {
        public const int MessageId = 4;

        public OrderSendScheme()
            : base(MessageId)
        {
            Model.Add(typeof(Currency), true)
                .AddAvailableCurrencies();

            Model.Add(typeof(Symbol), true)
                .AddAvailableSymbols();

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
                .AddRequired(nameof(Order.Fee))
                .AddRequired(nameof(Order.RedeemFee))
                .AddRequired(nameof(Order.Side))
                .AddRequired(nameof(Order.Type))
                .AddRequired(nameof(Order.FromWallets))
                .AddRequired(nameof(Order.ToWallet))
                .AddRequired(nameof(Order.RefundWallet))
                .AddRequired(nameof(Order.IsStayAfterDisconnect));
        }
    }
}