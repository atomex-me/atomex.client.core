using System.IO;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Atomix.Api.Proto
{
    public class ProtoScheme
    {
        public static ProtoScheme AuthNonce = new AuthNonceScheme();
        public static ProtoScheme Auth = new AuthScheme();
        public static ProtoScheme AuthOk = new AuthOkScheme();
        public static ProtoScheme Error = new ErrorScheme();
        public static ProtoScheme OrderSend = new OrderSendScheme();
        public static ProtoScheme OrderCancel = new OrderCancelScheme();
        public static ProtoScheme OrderStatus = new OrderStatusScheme();
        public static ProtoScheme Orders = new OrdersScheme();
        public static ProtoScheme ExecutionReport = new ExecutionReportScheme();
        public static ProtoScheme Swap = new SwapDataScheme();
        public static ProtoScheme Subscribe = new SubscribeScheme();
        public static ProtoScheme Unsubscribe = new UnsubscribeScheme();
        public static ProtoScheme Quotes = new QuotesScheme(); 
        public static ProtoScheme Entries = new EntriesScheme();
        public static ProtoScheme Snapshot = new SnapshotScheme();
        public static ProtoScheme OrderLog = new OrderLogScheme();

        protected RuntimeTypeModel Model { get; } = TypeModel.Create();
        private readonly byte _messageId;

        public ProtoScheme(byte messageId)
        {
            _messageId = messageId;
        }

        public T Deserialize<T>(MemoryStream stream) where T : class
        {
            return Model.Deserialize(stream, null, typeof(T)) as T;
        }

        public T DeserializeWithLengthPrefix<T>(MemoryStream stream, PrefixStyle prefixStyle, int expectedField) where T : class
        {
            return Model.DeserializeWithLengthPrefix(stream, null, typeof(T), prefixStyle, expectedField) as T;
        }

        public T DeserializeWithLengthPrefix<T>(MemoryStream stream) where T : class
        {
            return DeserializeWithLengthPrefix<T>(stream, PrefixStyle.Fixed32, 0);
        }

        public void Serialize<T>(Stream outputStream, T obj)
        {
            Model.Serialize(outputStream, obj);
        }

        public byte[] Serialize<T>(T obj)
        {
            var stream = new MemoryStream();
            Model.Serialize(stream, obj);
            return stream.ToArray();
        }

        public void SerializeWithLengthPrefix<T>(Stream outputStream, T obj, PrefixStyle prefixStyle, int field)
        {
            Model.SerializeWithLengthPrefix(outputStream, obj, typeof(T), prefixStyle, field);
        }

        public byte[] SerializeWithLengthPrefix<T>(T obj, PrefixStyle prefixStyle, int field)
        {
            var stream = new MemoryStream();
            Model.SerializeWithLengthPrefix(stream, obj, typeof(T), prefixStyle, field);
            return stream.ToArray();
        }

        public byte[] SerializeWithLengthPrefix<T>(T obj)
        {
            return SerializeWithLengthPrefix(obj, PrefixStyle.Fixed32, 0);
        }

        public void SerializeWithMessageId<T>(Stream outputStream, T obj, byte messageId)
        {
            outputStream.WriteByte(messageId);
            SerializeWithLengthPrefix(outputStream, obj, PrefixStyle.Fixed32, 0);
        }

        public byte[] SerializeWithMessageId<T>(T obj, byte messageId)
        {
            var stream = new MemoryStream();
            SerializeWithMessageId(stream, obj, messageId);
            return stream.ToArray();
        }

        public byte[] SerializeWithMessageId<T>(T obj)
        {
            return SerializeWithMessageId(obj, _messageId);
        }

        public string GetSchema<T>()
        {
            return Model.GetSchema(typeof(T), ProtoSyntax.Proto2);
        }
    }
}