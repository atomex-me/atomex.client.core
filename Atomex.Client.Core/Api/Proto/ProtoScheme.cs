using System.IO;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Atomex.Api.Proto
{
    public class ProtoScheme<T>
    {
        protected RuntimeTypeModel Model { get; }

        public byte MessageId { get; }

        protected ProtoScheme(byte messageId)
        {
            MessageId = messageId;
            Model = TypeModel.Create();
            Model.IncludeDateTimeKind = true;
        }

        public T Deserialize(MemoryStream stream)
        {
            return (T)Model.Deserialize(stream, null, typeof(T));
        }

        private T DeserializeWithLengthPrefix(
            Stream stream,
            PrefixStyle prefixStyle,
            int expectedField)
        {
            return (T)Model.DeserializeWithLengthPrefix(stream, null, typeof(T), prefixStyle, expectedField);
        }

        public T DeserializeWithLengthPrefix(MemoryStream stream)
        {
            return DeserializeWithLengthPrefix(stream, PrefixStyle.Fixed32, 0);
        }

        public void Serialize(Stream outputStream, T obj)
        {
            Model.Serialize(outputStream, obj);
        }

        public byte[] Serialize(T obj)
        {
            using (var stream = new MemoryStream())
            {
                Model.Serialize(stream, obj);
                return stream.ToArray();
            }
        }

        private void SerializeWithLengthPrefix(
            Stream outputStream,
            T obj,
            PrefixStyle prefixStyle,
            int field)
        {
            Model.SerializeWithLengthPrefix(outputStream, obj, typeof(T), prefixStyle, field);
        }

        private byte[] SerializeWithLengthPrefix(T obj, PrefixStyle prefixStyle, int field)
        {
            using (var stream = new MemoryStream())
            {
                Model.SerializeWithLengthPrefix(stream, obj, typeof(T), prefixStyle, field);
                return stream.ToArray();
            }
        }

        public byte[] SerializeWithLengthPrefix(T obj)
        {
            return SerializeWithLengthPrefix(obj, PrefixStyle.Fixed32, 0);
        }

        private void SerializeWithMessageId(Stream outputStream, T obj, byte messageId)
        {
            outputStream.WriteByte(messageId);
            SerializeWithLengthPrefix(outputStream, obj, PrefixStyle.Fixed32, 0);
        }

        private byte[] SerializeWithMessageId(T obj, byte messageId)
        {
            using (var stream = new MemoryStream())
            {
                SerializeWithMessageId(stream, obj, messageId);
                return stream.ToArray();
            }
        }

        public byte[] SerializeWithMessageId(T obj)
        {
            return SerializeWithMessageId(obj, MessageId);
        }

        public string GetSchema()
        {
            return Model.GetSchema(typeof(T), ProtoSyntax.Proto2);
        }
    }
}