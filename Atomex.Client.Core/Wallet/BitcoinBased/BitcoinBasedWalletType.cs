using System;
using NBitcoin;

namespace Atomex.Wallets.BitcoinBased
{
    public static class BitcoinBasedWalletType
    {
        public const int Legacy = 0;
        public const int Segwit = 1;
        public const int SegwitP2SH = 2;

        public static ScriptPubKeyType ToScriptPubKeyType(int walletType)
        {
            return walletType switch
            {
                Legacy => ScriptPubKeyType.Legacy,
                Segwit => ScriptPubKeyType.Segwit,
                SegwitP2SH => ScriptPubKeyType.SegwitP2SH,
                _ => throw new NotSupportedException($"Bitcoin based wallet type {@walletType} not supported.")
            };
        }
    }
}