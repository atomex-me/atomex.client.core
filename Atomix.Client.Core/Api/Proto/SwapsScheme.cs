using Atomix.Common.Proto;
using Atomix.Core;
using Atomix.Core.Entities;

namespace Atomix.Api.Proto
{
    public class SwapsScheme : ProtoScheme<Request<ClientSwap>>
    {
        public SwapsScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Request<ClientSwap>), true)
                .AddRequired(nameof(Request<ClientSwap>.Id));
        }
    }
}