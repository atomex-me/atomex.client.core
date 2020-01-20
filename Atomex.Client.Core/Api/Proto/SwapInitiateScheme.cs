using Atomex.Common.Proto;
using Atomex.Core;

namespace Atomex.Api.Proto
{
    public class SwapInitiateScheme : ProtoScheme<Swap>
    {
        public SwapInitiateScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Symbol), true)
                .AddRequired(nameof(Symbol.Name));

            Model.Add(typeof(Swap), true)
                .AddRequired(nameof(Swap.Id))
                .AddRequired(nameof(Swap.SecretHash))
                .AddRequired(nameof(Swap.Symbol))
                .AddRequired(nameof(Swap.ToAddress))
                .AddRequired(nameof(Swap.RewardForRedeem));
        }
    }
}