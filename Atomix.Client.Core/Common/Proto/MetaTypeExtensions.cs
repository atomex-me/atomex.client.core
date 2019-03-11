using ProtoBuf.Meta;

namespace Atomix.Common.Proto
{
    public static class MetaTypeExtensions
    {
        public static MetaType AddRequired(this MetaType metaType, int fieldNumber, string memberName)
        {
            var field = metaType.AddField(fieldNumber, memberName);
            field.IsRequired = true;

            return metaType;
        }

        public static MetaType AddRequired(this MetaType metaType, string memberName)
        {
            return metaType.AddRequired(metaType.GetFields().Length + 1, memberName);
        }

        public static MetaType AddAvailableCurrencies(this MetaType metaType)
        {
            for (var i = 0; i < Currencies.Available.Length; ++i)
                metaType.AddSubType(i + 1, Currencies.Available[i].GetType());

            return metaType;
        }

        public static MetaType AddAvailableSymbols(this MetaType metaType)
        {
            for (var i = 0; i < Symbols.Available.Length; ++i)
                metaType.AddSubType(i + 1, Symbols.Available[i].GetType());

            return metaType;
        }
    }
}