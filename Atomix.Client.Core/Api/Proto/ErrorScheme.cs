using Atomix.Common.Proto;
using Atomix.Core;

namespace Atomix.Api.Proto
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