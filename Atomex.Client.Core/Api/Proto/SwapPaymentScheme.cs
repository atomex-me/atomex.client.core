using Atomex.Common.Proto;
using Atomex.Core;

namespace Atomex.Api.Proto
{
    public class SwapPaymentScheme : ProtoScheme<Swap>
    {
        public SwapPaymentScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Symbol), true)
                .AddRequired(nameof(Symbol.Name));

            Model.Add(typeof(Swap), true)
                .AddRequired(nameof(Swap.Id))
                .AddRequired(nameof(Swap.Symbol))
                .AddRequired(nameof(Swap.PaymentTxId))
                .AddRequired(nameof(Swap.RedeemScript));
        }
    }
}