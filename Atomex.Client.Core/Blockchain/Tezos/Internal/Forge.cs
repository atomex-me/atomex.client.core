using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

using Serilog;
using Newtonsoft.Json.Linq;

using Atomex.Common;
using Atomex.Cryptography;

namespace Atomex.Blockchain.Tezos.Internal
{
    public class Forge
    {
        public static Dictionary<string, int> OperationTags = new Dictionary<string, int>
        {
            {"endorsement", 0 },
            {"proposal", 5 },
            {"ballot", 6 },
            {"seed_nonce_revelation", 1 },
            {"double_endorsement_evidence", 2 },
            {"double_baking_evidence", 3 },
            {"activate_account", 4 },
            {"reveal", 107 },
            {"transaction", 108 },
            {"origination", 109 },
            {"delegation", 110 }
        };

        public static JToken ForgeOperationsLocal(string blockHeadHash, JToken operations)
        {
            if (!(operations is JArray arrOps))
                arrOps = new JArray(operations);

            var res = blockHeadHash != null
                ? Base58Check.Decode(blockHeadHash, Prefix.b).ToHexString()
                : "";

            foreach (JObject op in arrOps)
            {
                switch (op["kind"].ToString())
                {
                    case "reveal":
                        res += ForgeReveal(op);
                        break;
                    case "transaction":
                        res += ForgeTransaction(op);
                        break;
                    //todo:
                    //case "activate_account":
                    //    res += forge_activate_account(op);
                    //    break;
                    //case "origination":
                    //    res += forge_origination(op);
                    //    break;
                    case "delegation":
                        res += ForgeDelegation(op);
                        break;
                    default:
                        Log.Error("Not implemented forge error");
                        //
                        break;
                }
            }
            res = res.ToLower();
            return res;
        }

        private static string ForgeReveal(JObject op)
        {
            var res = ForgeNat((ulong)OperationTags[op["kind"].ToString()]);

            res += ForgeSource(op["source"].ToString());
            res += ForgeNat(op["fee"].Value<ulong>());
            res += ForgeNat(op["counter"].Value<ulong>());
            res += ForgeNat(op["gas_limit"].Value<ulong>());
            res += ForgeNat(op["storage_limit"].Value<ulong>());
            res += ForgePublicKey(op["public_key"].ToString());

            return res;
        }

        private static string ForgeTransaction(JObject op)
        {
            var res = ForgeNat((ulong)OperationTags[op["kind"].ToString()]);
            res += ForgeSource(op["source"].ToString());
            res += ForgeNat(op["fee"].Value<ulong>());
            res += ForgeNat(op["counter"].Value<ulong>());
            res += ForgeNat(op["gas_limit"].Value<ulong>());
            res += ForgeNat(op["storage_limit"].Value<ulong>());
            res += ForgeNat(op["amount"].Value<ulong>());
            res += ForgeAddress(op["destination"].ToString());

            if (op["parameters"] != null)
            {
                res += ForgeBool(true);
                res += ForgeMichelson.ForgeEntrypoint(op["parameters"]["entrypoint"].Value<string>());
                res += ForgeArray(ForgeMichelson.ForgeMicheline(op["parameters"]["value"]));
            }
            else
                res += ForgeBool(false);

            return res;
        }

        private static string ForgeDelegation(JObject op)
        {
            var res = ForgeNat((ulong)OperationTags[op["kind"].ToString()]);
            res += ForgeSource(op["source"].ToString());
            res += ForgeNat(op["fee"].Value<ulong>());
            res += ForgeNat(op["counter"].Value<ulong>());
            res += ForgeNat(op["gas_limit"].Value<ulong>());
            res += ForgeNat(op["storage_limit"].Value<ulong>());


            if (op["delegate"] != null)
            {
                res += ForgeBool(true);
                res += ForgeSource(op["delegate"].ToString());
            }
            else
            {
                res += ForgeBool(false);
            }

            return res;
        }
        public static string ForgeArray(string value)
        {
            var bytes = BitConverter.GetBytes(value.Length / 2).Reverse().ToArray();
            return bytes.ToHexString() + value;
        }

        private static string ForgeNat(ulong value)
        {
            if (value < 0)
                throw new ArgumentException("Value cannot be negative", nameof(value));

            var buf = new List<byte>();

            var more = true;

            while (more)
            {
                byte b = (byte)(value & 0x7f);
                value >>= 7;
                if (value > 0)
                    b |= 0x80;
                else
                    more = false;

                buf.Add(b);
            }

            return buf.ToArray().ToHexString();
        }

        public static string ForgeAddress(string value)
        {
            var prefix = value.Substring(0, 3);

            var res = Base58Check.Decode(value).ToHexString().Substring(6);

            return prefix switch
            {
                "tz1" => "0000" + res,
                "tz2" => "0001" + res,
                "tz3" => "0002" + res,
                "KT1" => "01" + res + "00",
                _     => throw new Exception($"Value address exception. Invalid prefix {prefix}")
            };
        }

        private static string ForgeSource(string value)
        {
            var prefix = value.Substring(0, 3);

            var res = Base58Check.Decode(value).ToHexString().Substring(6);

            return prefix switch
            {
                "tz1" => "00" + res,
                "tz2" => "01" + res,
                "tz3" => "02" + res,
                _ => throw new Exception($"Value source exception. Invalid prefix {prefix}")
            };
        }

        private static string ForgeBool(bool value) =>
            value ? "FF" : "00";

        private static string ForgePublicKey(string value)
        {
            var prefix = value.Substring(0, 4);

            var res = Base58Check.Decode(value).ToHexString().Substring(8);

            return prefix switch
            {
                "edpk" => "00" + res,
                "sppk" => "01" + res,
                "p2pk" => "02" + res,
                _ => throw new Exception($"Value public_key exception. Invalid prefix {prefix}")
            };
        }

    }

    public class ForgeMichelson
    {
        public static string ForgeInt(int value)
        {
            var binary = Convert.ToString(Math.Abs(value), 2);

            int pad = 6;
            if ((binary.Length - 6) % 7 == 0)
                pad = binary.Length;
            else if (binary.Length > 6)
                pad = binary.Length + 7 - (binary.Length - 6) % 7;

            binary = binary.PadLeft(pad, '0');

            var septets = new List<string>();

            for (int i = 0; i <= pad / 7; i++)
                septets.Add(binary.Substring(7 * i, Math.Min(7, pad - 7 * i)));

            septets.Reverse();

            septets[0] = (value >= 0 ? "0" : "1") + septets[0];

            string res = "";

            for (int i = 0; i < septets.Count; i++)
            {
                var prefix = i == septets.Count - 1
                    ? "0"
                    : "1";

                res += Convert.ToByte(prefix + septets[i], 2).ToString("X2");
            }

            return res;
        }

        public static string ForgeMicheint(BigInteger value)
        {
            var abs = BigInteger.Abs(value);
            var res = new List<byte>
            {
                (byte)(value.Sign < 0 ? (abs & 0x3F | 0x40) : (abs & 0x3F))
            };

            abs >>= 6;

            while (abs > 0)
            {
                res[res.Count - 1] |= 0x80;
                res.Add((byte)(abs & 0x7F));
                abs >>= 7;
            }

            return res.ToArray().ToHexString();
        }

        public static string ForgeEntrypoint(string value)
        {
            string res = "";

            if (EntrypointTags.ContainsKey(value))
            {
                res += EntrypointTags[value].ToString("X2");
            }
            else
            {
                res += "ff";
                res += Forge.ForgeArray(Encoding.Default.GetBytes(value).ToHexString());
            }

            return res;
        }

        public static string ForgeMicheline(JToken data)
        {
            string res = "";

            if (data is JArray)
            {
                res += "02";
                res += Forge.ForgeArray(string.Concat(data.Select(item => ForgeMicheline(item))));
            }
            else if (data is JObject)
            {
                if (data["prim"] != null)
                {
                    var args_len = data["args"]?.Count() ?? 0;
                    var annots_len = data["annots"]?.Count() ?? 0;

                    res += LenTags[args_len][annots_len > 0];
                    res += PrimTags[data["prim"].ToString()];

                    if (args_len > 0)
                    {
                        string args = string.Concat(data["args"].Select(item => ForgeMicheline(item)));
                        if (args_len < 3)
                            res += args;
                        else
                            res += Forge.ForgeArray(args);
                    }

                    if (annots_len > 0)
                        res += Forge.ForgeArray(string.Join(" ", data["annots"]));
                    else if (args_len == 3)
                        res += new string('0', 8);
                }
                else if (data["bytes"] != null)
                {
                    res += "0A";
                    res += Forge.ForgeArray(data["bytes"].ToString());
                }
                else if (data["int"] != null)
                {
                    res += "00";
                    res += ForgeMicheint(BigInteger.Parse(data["int"].Value<string>()));
                }
                else if (data["string"] != null)
                {
                    res += "01";
                    res += Forge.ForgeArray(Encoding.Default.GetBytes(data["string"].Value<string>()).ToHexString());
                }
                else
                {
                    throw new Exception($"Michelson forge error");
                }
            }
            else
            {
                throw new Exception($"Michelson forge error");
            }

            return res;
        }

        private static Dictionary<bool, string>[] LenTags = new Dictionary<bool, string>[]
        {
            new Dictionary<bool, string> {
                { false, "03" },
                { true, "04" }
            },
            new Dictionary<bool, string> {
                { false, "05" },
                { true, "06" }
            },
            new Dictionary<bool, string> {
                { false, "07" },
                { true, "08" }
            },
            new Dictionary<bool, string> {
                { false, "09" },
                { true, "09" }
            }
        };

        public static Dictionary<string, int> EntrypointTags = new Dictionary<string, int> {
            {"default", 0 },
            {"root", 1 },
            {"do", 2 },
            {"set_delegate", 3 },
            {"remove_delegate", 4 }
        };

        private static Dictionary<string, string> PrimTags = new Dictionary<string, string> {
            {"parameter", "00" },
            {"storage", "01" },
            {"code", "02" },
            {"False", "03" },
            {"Elt", "04" },
            {"Left", "05" },
            {"None", "06" },
            {"Pair", "07" },
            {"Right", "08" },
            {"Some", "09" },
            {"True", "0A" },
            {"Unit", "0B" },
            {"PACK", "0C" },
            {"UNPACK", "0D" },
            {"BLAKE2B", "0E" },
            {"SHA256", "0F" },
            {"SHA512", "10" },
            {"ABS", "11" },
            {"ADD", "12" },
            {"AMOUNT", "13" },
            {"AND", "14" },
            {"BALANCE", "15" },
            {"CAR", "16" },
            {"CDR", "17" },
            {"CHECK_SIGNATURE", "18" },
            {"COMPARE", "19" },
            {"CONCAT", "1A" },
            {"CONS", "1B" },
            {"CREATE_ACCOUNT", "1C" },
            {"CREATE_CONTRACT", "1D" },
            {"IMPLICIT_ACCOUNT", "1E" },
            {"DIP", "1F" },
            {"DROP", "20" },
            {"DUP", "21" },
            {"EDIV", "22" },
            {"EMPTY_MAP", "23" },
            {"EMPTY_SET", "24" },
            {"EQ", "25" },
            {"EXEC", "26" },
            {"FAILWITH", "27" },
            {"GE", "28" },
            {"GET", "29" },
            {"GT", "2A" },
            {"HASH_KEY", "2B" },
            {"IF", "2C" },
            {"IF_CONS", "2D" },
            {"IF_LEFT", "2E" },
            {"IF_NONE", "2F" },
            {"INT", "30" },
            {"LAMBDA", "31" },
            {"LE", "32" },
            {"LEFT", "33" },
            {"LOOP", "34" },
            {"LSL", "35" },
            {"LSR", "36" },
            {"LT", "37" },
            {"MAP", "38" },
            {"MEM", "39" },
            {"MUL", "3A" },
            {"NEG", "3B" },
            {"NEQ", "3C" },
            {"NIL", "3D" },
            {"NONE", "3E" },
            {"NOT", "3F" },
            {"NOW", "40" },
            {"OR", "41" },
            {"PAIR", "42" },
            {"PUSH", "43" },
            {"RIGHT", "44" },
            {"SIZE", "45" },
            {"SOME", "46" },
            {"SOURCE", "47" },
            {"SENDER", "48" },
            {"SELF", "49" },
            {"STEPS_TO_QUOTA", "4A" },
            {"SUB", "4B" },
            {"SWAP", "4C" },
            {"TRANSFER_TOKENS", "4D" },
            {"SET_DELEGATE", "4E" },
            {"UNIT", "4F" },
            {"UPDATE", "50" },
            {"XOR", "51" },
            {"ITER", "52" },
            {"LOOP_LEFT", "53" },
            {"ADDRESS", "54" },
            {"CONTRACT", "55" },
            {"ISNAT", "56" },
            {"CAST", "57" },
            {"RENAME", "58" },
            {"bool", "59" },
            {"contract", "5A" },
            {"int", "5B" },
            {"key", "5C" },
            {"key_hash", "5D" },
            {"lambda", "5E" },
            {"list", "5F" },
            {"map", "60" },
            {"big_map", "61" },
            {"nat", "62" },
            {"option", "63" },
            {"or", "64" },
            {"pair", "65" },
            {"set", "66" },
            {"signature", "67" },
            {"string", "68" },
            {"bytes", "69" },
            {"mutez", "6A" },
            {"timestamp", "6B" },
            {"unit", "6C" },
            {"operation", "6D" },
            {"address", "6E" },
            {"SLICE", "6F" }
        };
    }
}