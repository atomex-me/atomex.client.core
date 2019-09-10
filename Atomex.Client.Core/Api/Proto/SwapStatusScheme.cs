using Atomex.Common.Proto;
using Atomex.Core;
using Atomex.Core.Entities;

namespace Atomex.Api.Proto
{
    public class SwapStatusScheme : ProtoScheme<Request<ClientSwap>>
    {
        public SwapStatusScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(ClientSwap), true)
                .AddRequired(nameof(ClientSwap.Id));

            Model.Add(typeof(Request<ClientSwap>), true)
                .AddRequired(nameof(Request<ClientSwap>.Id))
                .AddRequired(nameof(Request<ClientSwap>.Data));
        }
    }
}