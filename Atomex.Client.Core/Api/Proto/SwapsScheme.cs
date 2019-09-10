using Atomex.Common.Proto;
using Atomex.Core;
using Atomex.Core.Entities;

namespace Atomex.Api.Proto
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