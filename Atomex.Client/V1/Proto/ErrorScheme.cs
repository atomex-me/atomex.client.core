using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Proto
{
    public class ErrorScheme : ProtoScheme<Error>
    {
        public ErrorScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Error), true)
                .AddRequired(nameof(Error.Code))
                .AddRequired(nameof(Error.Description))
                .AddRequired(nameof(Error.RequestId))
                .AddRequired(nameof(Error.OrderId))
                .AddRequired(nameof(Error.SwapId));
        }
    }
}