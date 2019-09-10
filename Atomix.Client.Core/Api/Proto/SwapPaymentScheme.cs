using Atomix.Common.Proto;
using Atomix.Core.Entities;

namespace Atomix.Api.Proto
{
    public class SwapPaymentScheme : ProtoScheme<ClientSwap>
    {
        public SwapPaymentScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Symbol), true)
                .AddRequired(nameof(Symbol.Name));

            Model.Add(typeof(ClientSwap), true)
                .AddRequired(nameof(ClientSwap.Id))
                .AddRequired(nameof(ClientSwap.Symbol))
                .AddRequired(nameof(ClientSwap.PaymentTxId));
        }
    }
}