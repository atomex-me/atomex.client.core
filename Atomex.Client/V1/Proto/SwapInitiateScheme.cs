using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Proto
{
    public class SwapInitiateScheme : ProtoScheme<Swap>
    {
        public SwapInitiateScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Swap), true)
                .AddRequired(nameof(Swap.Id))
                .AddRequired(nameof(Swap.SecretHash))
                .AddRequired(nameof(Swap.Symbol))
                .AddRequired(nameof(Swap.ToAddress))
                .AddRequired(nameof(Swap.RewardForRedeem))
                .AddRequired(nameof(Swap.RefundAddress));
        }
    }
}