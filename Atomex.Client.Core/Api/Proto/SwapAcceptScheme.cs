using Atomex.Common.Proto;
using Atomex.Core.Entities;

namespace Atomex.Api.Proto
{
    public class SwapAcceptScheme : ProtoScheme<ClientSwap>
    {
        public SwapAcceptScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Symbol), true)
                .AddRequired(nameof(Symbol.Name));

            Model.Add(typeof(ClientSwap), true)
                .AddRequired(nameof(ClientSwap.Id))
                .AddRequired(nameof(ClientSwap.Symbol))
                .AddRequired(nameof(ClientSwap.ToAddress))
                .AddRequired(nameof(ClientSwap.RewardForRedeem));
        }
    }
}