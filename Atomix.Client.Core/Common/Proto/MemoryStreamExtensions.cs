using System.IO;
using Atomix.Api.Proto;

namespace Atomix.Common.Proto
{
    public static class MemoryStreamExtensions
    {
        public static T Deserialize<T>(this MemoryStream stream, ProtoScheme scheme) where T : class
        {
            return scheme.DeserializeWithLengthPrefix<T>(stream);
        }
    }
}