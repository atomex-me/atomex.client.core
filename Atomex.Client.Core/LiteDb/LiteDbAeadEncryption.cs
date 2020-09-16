using System;
using System.Text;
using LiteDB;

using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Cryptography.Abstract;

namespace Atomex.LiteDb
{
    public static class LiteDbAeadEncryption
    {
        private const int NonceSize = 12;

        public static byte[] GetShadowedId(
            ReadOnlySpan<byte> key,
            string id)
        {
            return MacAlgorithm.HmacSha256
                .Mac(key, Encoding.UTF8.GetBytes("id" + id));
        }

        public static string GetShadowedIdHex(
            ReadOnlySpan<byte> key,
            string id) => GetShadowedId(key, id).ToHexString();

        public static BsonDocument EncryptDocument(
            ReadOnlySpan<byte> key,
            BsonDocument document,
            byte[] associatedData)
        {
            var shadowedId = GetShadowedId(key, document["_id"].AsString);

            using var ki = new UnmanagedBytes(32);
            MacAlgorithm.HmacSha256.Mac(key, shadowedId, ki);

            var nonce = Rand.SecureRandomBytes(NonceSize);
            var serialized = BsonSerializer.Serialize(document);

            var encrypted = AeadAlgorithm.Aes256Gcm.Encrypt(
                key: ki,
                nonce: nonce,
                associatedData: associatedData,
                plaintext: serialized);

            return new BsonDocument
            {
                ["_id"] = shadowedId.ToHexString(),
                ["Data"] = nonce.ConcatArrays(encrypted)
            };
        }

        public static BsonDocument DecryptDocument(
            ReadOnlySpan<byte> key,
            BsonDocument document,
            byte[] associatedData)
        {
            var shadowedId = Hex.FromString(document["_id"].AsString);

            using var ki = new UnmanagedBytes(32);
            MacAlgorithm.HmacSha256.Mac(key, shadowedId, ki);

            var data = new ReadOnlySpan<byte>(document["Data"].AsBinary);

            var serialized = new byte[data.Length - NonceSize];

            var result = AeadAlgorithm.Aes256Gcm.Decrypt(
                key: ki,
                nonce: data.Slice(0, NonceSize),
                associatedData: associatedData,
                ciphertext: data.Slice(NonceSize),
                plaintext: serialized);

            if (!result || serialized == null)
                throw new Exception("Decryption error");

            return BsonSerializer.Deserialize(serialized, utcDate: true);
        }
    }
}