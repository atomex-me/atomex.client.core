using System;
using System.IO;

namespace Atomix.Blockchain.Bitcoin.Own
{
    public class BtcScript
    {
        public const byte SigHashAll = 0x01;

        // constants
        public const byte Op0 = 0x00;
        public const byte OpFalse = 0x00;
        public const byte Op75 = 0x4B;
        public const byte OpPushData1 = 0x4C;
        public const byte OpPushData2 = 0x4D;
        public const byte Op1Negate = 0x4F;
        public const byte OpPushData4 = 0x4E;
        public const byte Op1 = 0x51;
        public const byte OpTrue = 0x51;

        // flow control
        public const byte OpNop = 0x61;
        public const byte OpIf = 0x63;
        public const byte OpNotIf = 0x64;
        public const byte OpElse = 0x67;
        public const byte OpEndIf = 0x68;
        public const byte OpVerify = 0x69;
        public const byte OpReturn = 0x6a;

        // stack
        public const byte OpDrop = 0x75;
        public const byte OpDup = 0x76;
        public const byte OnPick = 0x79;
        public const byte OnRoll = 0x7A;
        public const byte OnSwap = 0x7C;
        public const byte On2Dup = 0x6E;
        public const byte On3Dup = 0x6F;

        // bitwise logic
        public const byte OpEqual = 0x87;
        public const byte OpEqualVerify = 0x88;
        
        // arithmetic
        public const byte OpNot = 0x91;
        public const byte OpAdd = 0x93;
        public const byte OpSub = 0x94;
        public const byte OpMul = 0x95;
        public const byte OpDiv = 0x96;
        public const byte OpMod = 0x97;
        public const byte OpLShift = 0x98;
        public const byte OpRShift = 0x99;
        
        // crypto
        public const byte OpRipemd160 = 0xA6;
        public const byte OpSha1 = 0xA7;
        public const byte OpSha256 = 0xA8;
        public const byte OpHash160 = 0xA9;
        public const byte OpHash256 = 0xAA;
        public const byte OpCheckSig = 0xAC;
        public const byte OpCheckSigVerify = 0xAD;
        public const byte OpCheckMultiSig = 0xAE;
        public const byte OpCheckMultiSigVerify = 0xAF;

        // locktime
        public const byte OpCheckLockTimeVerify = 0xB1;
        public const byte OpCheckSequenceVerify = 0xB2;

        private readonly MemoryStream _stream = new MemoryStream();

        public void Push(byte opCode)
        {
            _stream.WriteByte(opCode);
        }

        public void Push(byte[] data, bool isLengthPrefixed = true)
        {
            if (isLengthPrefixed)
                if (data.Length <= Op75)
                    _stream.WriteByte((byte)data.Length);
                else
                    throw new Exception($"Data size must be equal or less than {Op75}.");

            _stream.Write(data, 0, data.Length);
        }

        public byte[] GetBytes()
        {
            return _stream.ToArray();
        }

        public static byte[] P2PkhUnlocking(byte[] signature, byte[] publicKey)
        {
            // <signature> <pubKey>

            var script = new BtcScript();

            script.Push((byte)(signature.Length + 1));
            script.Push(signature, false);
            script.Push(SigHashAll);
            script.Push(publicKey);

            return script.GetBytes();
        }

        public static byte[] P2PkhLocking(byte[] publicKeyHash)
        {
            // OP_DUP OP_HASH160 <publicKeyHash> OP_EQUALVERIFY OP_CHECKSIG

            var script = new BtcScript();

            script.Push(OpDup);
            script.Push(OpHash160);
            script.Push(publicKeyHash);
            script.Push(OpEqualVerify);
            script.Push(OpCheckSig);

            return script.GetBytes();
        }

        public static byte[] SwapLocking(byte[] publicKeyA, byte[] publicKeyB, byte[] secretHash)
        {
            // OP_IF
            //    2 <publicKeyA> <publicKeyB> 2 CHECKMULTISIGVERIFY                # refund for A
            // OP_ELSE
            //    OP_HASH160 <secretHash> OP_EQUAL <publicKeyB> OP_CHECKSIGVERIFY  # payment for B
            // OP_ENDIF

            var script = new BtcScript();

            script.Push(OpIf);
            script.Push(2);
            script.Push(publicKeyA);
            script.Push(publicKeyB);
            script.Push(2);
            script.Push(OpCheckMultiSigVerify);
            script.Push(OpElse);
            script.Push(OpHash160);
            script.Push(secretHash);
            script.Push(OpEqual);
            script.Push(publicKeyB);
            script.Push(OpCheckSigVerify);
            script.Push(OpEndIf);

            return script.GetBytes();
        }

        public static byte[] SwapRefundUnlocking(byte[] signA, byte[] signB)
        {
            // <signA> <signB> 1

            var script = new BtcScript();

            script.Push((byte)(signA.Length + 1));
            script.Push(signA, false);
            script.Push(SigHashAll);
            script.Push((byte)(signB.Length + 1));
            script.Push(signB, false);
            script.Push(SigHashAll);
            script.Push(1);

            return script.GetBytes();
        }

        public static byte[] SwapUnlocking(byte[] sign, byte[] secret)
        {
            // <sign> <secret> 0

            var script = new BtcScript();

            script.Push((byte)(sign.Length + 1));
            script.Push(sign, false);
            script.Push(SigHashAll);
            script.Push(secret);
            script.Push(0);

            return script.GetBytes();
        }
    }
}