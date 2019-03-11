namespace Atomix.Api.Proto
{
    public class OrdersScheme : ProtoScheme
    {
        public const int MessageId = 7;

        public OrdersScheme()
            : base(MessageId)
        {
            //Model.Add(typeof(Currency), true)
            //    .AddSubType(1, typeof(Bitcoin))
            //    .AddSubType(2, typeof(Litecoin));

            //Model.Add(typeof(Wallet), true)
            //    .AddRequired(1, nameof(Wallet.Address))
            //    .AddRequired(2, nameof(Wallet.Currency))
            //    .AddRequired(3, nameof(Wallet.PublicKey))
            //    .AddRequired(4, nameof(Wallet.ProofOfPossession))
            //    .AddRequired(5, nameof(Wallet.Salt));

            //Model.Add(typeof(Symbol), true)
            //    .AddSubType(1, typeof(BtcLtc));

            //Model.Add(typeof(Order), true)
            //    .AddRequired(1, nameof(Order.ClientOrderId))
            //    .AddRequired(2, nameof(Order.Symbol))
            //    .AddRequired(3, nameof(Order.TimeStamp))
            //    .AddRequired(4, nameof(Order.Price))
            //    .AddRequired(5, nameof(Order.Qty))
            //    .AddRequired(6, nameof(Order.Side))
            //    .AddRequired(7, nameof(Order.Type))
            //    .AddRequired(8, nameof(Order.Wallets));
        }
    }
}