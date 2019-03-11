using Atomix.Common.Proto;
using Atomix.Core;

namespace Atomix.Api.Proto
{
    public class ErrorScheme : ProtoScheme
    {
        public const int MessageId = 3;

        public ErrorScheme()
            : base(MessageId)
        {
            Model.Add(typeof(Error), true)
                .AddRequired(nameof(Core.Error.Code))
                .AddRequired(nameof(Core.Error.Description));
        }
    }
}