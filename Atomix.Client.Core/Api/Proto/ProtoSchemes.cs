using System;
using Atomix.Abstract;

namespace Atomix.Api.Proto
{
    public class ProtoSchemes
    {
        public AuthNonceScheme AuthNonce { get; }
        public AuthScheme Auth { get; }
        public AuthOkScheme AuthOk { get; }
        public ErrorScheme Error { get; }
        public OrderScheme Order { get; }
        public OrderSendScheme OrderSend { get; }
        public OrderCancelScheme OrderCancel { get; }
        public OrderStatusScheme OrderStatus { get; }
        public OrdersScheme Orders { get; }
        public SwapScheme Swap { get; }
        public SwapInitiateScheme SwapInitiate { get; }
        public SwapAcceptScheme SwapAccept { get; }
        public SwapPaymentScheme SwapPayment { get; }
        public SwapStatusScheme SwapStatus { get; }
        public SwapsScheme Swaps { get; }
        public SubscribeScheme Subscribe { get; }
        public UnsubscribeScheme Unsubscribe { get; }
        public QuotesScheme Quotes { get; }
        public EntriesScheme Entries { get; }
        public SnapshotScheme Snapshot { get; }
        public OrderLogScheme OrderLog { get; }

        public ICurrencies Currencies { get; }
        public ISymbols Symbols { get; }

        public ProtoSchemes(ICurrencies currencies, ISymbols symbols)
        {
            Currencies = currencies ?? throw new ArgumentNullException(nameof(currencies));
            Symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));

            byte id = 0;

            AuthNonce    = new AuthNonceScheme(messageId: id++);
            Auth         = new AuthScheme(messageId: id++);
            AuthOk       = new AuthOkScheme(messageId: id++);
            Error        = new ErrorScheme(messageId: id++);

            Order        = new OrderScheme(messageId: id++, currencies: currencies);
            OrderSend    = new OrderSendScheme(messageId: id++, currencies: currencies);
            OrderCancel  = new OrderCancelScheme(messageId: id++);
            OrderStatus  = new OrderStatusScheme(messageId: id++);
            Orders       = new OrdersScheme(messageId: id++);

            Swap         = new SwapScheme(messageId: id++, currencies: currencies);
            SwapInitiate = new SwapInitiateScheme(messageId: id++);
            SwapAccept   = new SwapAcceptScheme(messageId: id++);
            SwapPayment  = new SwapPaymentScheme(messageId: id++);
            SwapStatus   = new SwapStatusScheme(messageId: id++);
            Swaps        = new SwapsScheme(messageId: id++);

            Subscribe    = new SubscribeScheme(messageId: id++);
            Unsubscribe  = new UnsubscribeScheme(messageId: id++);
            Quotes       = new QuotesScheme(messageId: id++);
            Entries      = new EntriesScheme(messageId: id++);
            Snapshot     = new SnapshotScheme(messageId: id++);
            OrderLog     = new OrderLogScheme(messageId: id);
        }
    }
}