using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Proto
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