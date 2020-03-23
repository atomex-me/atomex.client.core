using ProtoBuf.Meta;

namespace Atomex.Common.Proto
{
    public static class MetaTypeExtensions
    {
        public static MetaType AddOptional(this MetaType metaType, int fieldNumber, string memberName)
        {
            var field = metaType.AddField(fieldNumber, memberName);
            field.IsRequired = false;

            return metaType;
        }

        public static MetaType AddRequired(this MetaType metaType, int fieldNumber, string memberName)
        {
            var field = metaType.AddField(fieldNumber, memberName);
            field.IsRequired = true;

            return metaType;
        }

        public static MetaType AddRequired(this MetaType metaType, string memberName)
        {
            var fieldsCount = metaType.GetFields().Length + metaType.GetSubtypes().Length;

            return metaType.AddRequired(fieldsCount + 1, memberName);
        }
    }
}