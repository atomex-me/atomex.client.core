using Atomex.Common.Proto;
using Atomex.Core;

namespace Atomex.Api.Proto
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