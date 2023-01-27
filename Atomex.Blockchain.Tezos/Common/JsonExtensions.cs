using System;
using System.Buffers;
using System.Text.Json;

namespace Atomex.Blockchain.Tezos.Common
{
    public static class JsonExtensions
    {
        public static T? ToObject<T>(this JsonElement element, JsonSerializerOptions? options = null)
        {
            var bufferWriter = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(bufferWriter))
                element.WriteTo(writer);

            return JsonSerializer.Deserialize<T>(bufferWriter.WrittenSpan, options);
        }

        public static T? ToObject<T>(this JsonDocument document, JsonSerializerOptions? options = null)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            return document.RootElement.ToObject<T>(options);
        }

        /// <summary>
        /// Get property with <paramref name="property"/> name from json <paramref name="element"/>. If json element is not an object or does not contain required property, the method returns null
        /// </summary>
        /// <remarks></remarks>
        /// <param name="element">Json element</param>
        /// <param name="property">Property name</param>
        /// <returns>JsonElement property if success, otherwise null</returns>
        public static JsonElement? Get(this JsonElement element, string property)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
                ? value
                : null;
        }

        /// <summary>
        /// Get child element by <paramref name="index"/> from json <paramref name="element"/>. If json element is not an array, the method returns null. If the index is out of range, the method throws an IndexOutOfRangeException
        /// </summary>
        /// <param name="element">Json element</param>
        /// <param name="index">Indexe</param>
        /// <returns></returns>
        /// <exception cref="IndexOutOfRangeException"/>
        public static JsonElement? Get(this JsonElement element, int index)
        {
            return element.ValueKind == JsonValueKind.Array
                ? element[index]
                : null;
        }

        public static JsonElement? LastOrDefault(this JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Array
                ? element[element.GetArrayLength() - 1]
                : null;
        }
    }
}