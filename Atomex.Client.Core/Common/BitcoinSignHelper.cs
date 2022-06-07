using System.IO;
using System.Text;

using NBitcoin;
using NBitcoin.Protocol;

using Atomex.Cryptography.Abstract;

namespace Atomex.Common
{
    public static class BitcoinSignHelper
    {
        private static readonly string BitcoinSignedMessageHeader = "Bitcoin Signed Message:\n";
        private static readonly byte[] BitcoinSignedMessageHeaderBytes = Encoding.UTF8.GetBytes(BitcoinSignedMessageHeader);

        public static byte[] MessageHash(byte[] messageBytes)
        {
            var ms = new MemoryStream();

            ms.WriteByte((byte)BitcoinSignedMessageHeaderBytes.Length);
            ms.Write(BitcoinSignedMessageHeaderBytes);
            ms.Write(new VarInt((ulong)messageBytes.Length).ToBytes());
            ms.Write(messageBytes);

            var messageForSigning = ms.ToArray();

            return HashAlgorithm.Sha256.Hash(messageForSigning, iterations: 2);
        }
    }
}