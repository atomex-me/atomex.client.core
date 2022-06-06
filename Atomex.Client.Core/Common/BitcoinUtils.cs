using System;
using System.IO;
using System.Text;

using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.Protocol;

using Atomex.Cryptography;

namespace Atomex.Common
{
    public static class BitcoinUtils
    {
        static readonly string BitcoinSignedMessageHeader = "Bitcoin Signed Message:\n";
        static readonly byte[] BitcoinSignedMessageHeaderBytes = Encoding.UTF8.GetBytes(BitcoinSignedMessageHeader);

        //http://bitcoinj.googlecode.com/git-history/keychain/core/src/main/java/com/google/bitcoin/core/Utils.java
        public static byte[] FormatMessageForSigning(byte[] messageBytes)
        {
            using var ms = new MemoryStream();

            ms.WriteByte((byte)BitcoinSignedMessageHeaderBytes.Length);
            ms.Write(BitcoinSignedMessageHeaderBytes);

            var size = new VarInt((ulong)messageBytes.Length);
            ms.Write(size.ToBytes());
            ms.Write(messageBytes, 0, messageBytes.Length);

            return ms.ToArray();
        }
    }
}
