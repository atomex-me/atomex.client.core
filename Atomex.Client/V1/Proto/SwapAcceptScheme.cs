using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Proto
{
    public class SwapAcceptScheme : ProtoScheme<Swap>
    {
        public SwapAcceptScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Swap), true)
                .AddRequired(nameof(Swap.Id))
                .AddRequired(nameof(Swap.Symbol))
                .AddRequired(nameof(Swap.ToAddress))
                .AddRequired(nameof(Swap.RewardForRedeem))
                .AddRequired(nameof(Swap.RefundAddress));
        }
    }
}