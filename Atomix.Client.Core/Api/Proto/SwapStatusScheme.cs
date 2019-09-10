using Atomix.Common.Proto;
using Atomix.Core;
using Atomix.Core.Entities;

namespace Atomix.Api.Proto
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