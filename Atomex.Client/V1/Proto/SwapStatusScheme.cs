using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Proto
{
    public class SwapStatusScheme : ProtoScheme<Request<Swap>>
    {
        public SwapStatusScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Swap), true)
                .AddRequired(nameof(Swap.Id));

            Model.Add(typeof(Request<Swap>), true)
                .AddRequired(nameof(Request<Swap>.Id))
                .AddRequired(nameof(Request<Swap>.Data));
        }
    }
}