using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Atomix.Common;
using Atomix.Cryptography;

namespace Atomix.Blockchain.Bitcoin.Own
{
    public abstract class BtcTxData
    {
        public byte[] GetBytes()
        {
            using (var stream = new MemoryStream())
            {
                ToBinaryWriter(new BinaryWriter(stream));
                return stream.ToArray();
            }
        }

        public abstract void ToBinaryWriter(BinaryWriter writer);
    }

    public class BtcOutPoint : BtcTxData
    {
        public const int HashLength = 32;

        public byte[] Hash { get; set; }
        public uint Index { get; set; }

        public override void ToBinaryWriter(BinaryWriter writer)
        {
            writer.Write(Hash.Reverse().ToArray());
            writer.Write(Index);
        }

        public static BtcOutPoint FromBinaryReader(BinaryReader reader)
        {
            return new BtcOutPoint
            {
                Hash = reader.ReadBytes(HashLength),
                Index = reader.ReadUInt32()
            };
        }
    }

    public class BtcTxIn : BtcTxData
    {
        public const uint DefaultSequence = 0xFFFFFFFF;

        public BtcOutPoint OutPoint { get; set; }
        public byte[] ScriptSignature { get; set; }
        public uint Sequence { get; set; } = DefaultSequence;

        public override void ToBinaryWriter(BinaryWriter writer)
        {
            OutPoint.ToBinaryWriter(writer);
            writer.Write(VarInteger.GetBytes((ulong)ScriptSignature.Length));
            writer.Write(ScriptSignature);
            writer.Write(Sequence);
        }

        public static BtcTxIn FromBinaryReader(BinaryReader reader)
        {
            return new BtcTxIn
            {
                OutPoint = BtcOutPoint.FromBinaryReader(reader),
                ScriptSignature = reader.ReadBytes((int)VarInteger.GetValue(reader)),
                Sequence = reader.ReadUInt32()
            };
        }
    }

    public class BtcTxOutput : BtcTxData, ITxOutput
    {
        public long Value { get; set; }
        public byte[] ScriptPubKey { get; set; }
        public uint Index { get; set; }
        public byte[] Hash { get; set; }

        public override void ToBinaryWriter(BinaryWriter writer)
        {
            writer.Write(Value);
            writer.Write(VarInteger.GetBytes((ulong)ScriptPubKey.Length));
            writer.Write(ScriptPubKey);
        }

        public static BtcTxOutput FromBinaryReader(BinaryReader reader)
        {
            return new BtcTxOutput
            {
                Value = reader.ReadInt64(),
                ScriptPubKey = reader.ReadBytes((int)VarInteger.GetValue(reader))
            };
        }
    }

    public class BtcTxWitness : BtcTxData
    {
        public List<byte[]> Components;

        public override void ToBinaryWriter(BinaryWriter writer)
        {
            writer.Write(VarInteger.GetBytes((ulong)Components.Count));

            foreach (var component in Components) {
                writer.Write(VarInteger.GetBytes((ulong)component.Length));
                writer.Write(component);
            }
        }

        public static BtcTxWitness FromBinaryReader(BinaryReader reader)
        {
            var components = new List<byte[]>();
            var count = (int)VarInteger.GetValue(reader);

            for (var i = 0; i < count; ++i)
                components.Add(reader.ReadBytes((int)VarInteger.GetValue(reader)));

            return new BtcTxWitness { Components = components };
        }
    }

    public class BtcTransaction : BtcTxData, IBlockchainTransaction
    {
        public const int DefaultVersion = 1;

        public int Version { get; set; } = DefaultVersion;
        public bool Flag => Witnesses != null && Witnesses.Count > 0;
        public List<BtcTxIn> Inputs { get; set; }
        public List<BtcTxOutput> Outputs { get; set; }
        public List<BtcTxWitness> Witnesses { get; set; }
        public uint LockTime { get; set; }

        public override void ToBinaryWriter(BinaryWriter writer)
        {
            writer.Write(Version);

            if (Flag) {
                writer.Write(0x00);
                writer.Write(0x01);
            }

            writer.Write(VarInteger.GetBytes((ulong)Inputs.Count));

            foreach (var input in Inputs)
                writer.Write(input.GetBytes());

            writer.Write(VarInteger.GetBytes((ulong)Outputs.Count));

            foreach (var output in Outputs)
                writer.Write(output.GetBytes());

            if (Flag)
                foreach (var witness in Witnesses)
                    witness.ToBinaryWriter(writer);

            writer.Write(LockTime);
        }

        public byte[] GetHash()
        {
            return GetHash(GetBytes());
        }

        public static byte[] GetHash(byte[] bytes)
        {
            return Sha256.Compute(Sha256.Compute(bytes));
        }

        public byte[] GetCheckSum()
        {
            return GetCheckSum(GetBytes());
        }

        public ITxOutput[] GetOutputs()
        {
            return Outputs.ToArray<ITxOutput>();
        }

        public static byte[] GetCheckSum(byte[] bytes)
        {
            return GetHash(bytes)  .SubArray(0, 4);
        }

        public static BtcTransaction FromBinaryReader(BinaryReader reader)
        {
            var hasWitnesses = false;
            var result = new BtcTransaction {
                Version = reader.ReadInt32()
            };

            var inputCount = (int)VarInteger.GetValue(reader);
            if (inputCount == 0) {
                var flag = reader.ReadByte();
                if (flag != 0x01)
                    throw new Exception("Invalid withness flag");

                hasWitnesses = true;
                inputCount = (int)VarInteger.GetValue(reader);
            }

            result.Inputs = new List<BtcTxIn>(inputCount);

            for (var i = 0; i < inputCount; ++i)
                result.Inputs.Add(BtcTxIn.FromBinaryReader(reader));

            var outputCount = (int)VarInteger.GetValue(reader);
            result.Outputs = new List<BtcTxOutput>(outputCount);

            for (var i = 0; i < outputCount; ++i)
                result.Outputs.Add(BtcTxOutput.FromBinaryReader(reader));

            if (hasWitnesses) {
                result.Witnesses = new List<BtcTxWitness>();

                for (var i = 0; i < inputCount; ++i)
                    result.Witnesses.Add(BtcTxWitness.FromBinaryReader(reader));
            }

            return result;
        }

        public static BtcTransaction FromBytes(byte[] bytes)
        {
            return FromBinaryReader(new BinaryReader(new MemoryStream(bytes)));
        }
    }

    public class BtcTxMessage : BtcTxData
    {
        public const uint Main = 0xD9B4BEF9;
        public const uint TestNet = 0xDAB5BFFA;
        public const uint TestNet3 = 0x0709110B;
        public const uint NameCoin = 0xFEB4BEF9;

        public const int CommandSize = 12;
        public const string CommandTx = "tx";

        public uint Magic { get; set; } = TestNet;
        public string Command { get; set; } = CommandTx;
        public BtcTransaction Tx { get; set; }
        public byte[] CheckSum { get; set; }

        public override void ToBinaryWriter(BinaryWriter writer)
        {
            writer.Write(Magic);
            writer.Write(Encoding.ASCII.GetBytes(Command, 0, Command.Length, new byte[CommandSize], 0));

            var txBytes = Tx.GetBytes();
            writer.Write((uint)txBytes.Length);
            writer.Write(BtcTransaction.GetCheckSum(txBytes));
            writer.Write(txBytes);
        }

        public static BtcTxMessage FromBinaryReader(BinaryReader reader)
        {
            var result = new BtcTxMessage {
                Magic = reader.ReadUInt32(),
                Command = Encoding.ASCII.GetString(reader.ReadBytes(CommandSize))
            };

            var payloadSize = (int)reader.ReadUInt32();
            result.CheckSum = reader.ReadBytes(4);

            var payload = reader.ReadBytes(payloadSize);
            result.Tx = BtcTransaction.FromBytes(payload);

            return result;
        }
    }
}