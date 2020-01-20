using Atomex.Common.Proto;
using Atomex.Core;

namespace Atomex.Api.Proto
{
    public class SwapsScheme : ProtoScheme<Request<Swap>>
    {
        public SwapsScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Request<Swap>), true)
                .AddRequired(nameof(Request<Swap>.Id));
        }
    }
}