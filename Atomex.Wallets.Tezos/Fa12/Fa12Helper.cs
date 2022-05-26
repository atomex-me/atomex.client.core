using System.Numerics;

namespace Atomex.Wallets.Tezos.Fa12
{
    public static class Fa12Helper
    {
        public static string TransferParameters(
            string from,
            string to,
            BigInteger amount)
        {
            return
            "{" +
                $"\"prim\":\"Pair\"," +
                $"\"args\":" +
                $"[" +
                    "{" +
                        $"\"string\":\"{from}\"" +
                    "}," +
                    "{" +
                        $"\"prim\":\"Pair\"," +
                        $"\"args\":" +
                        $"[" +
                            "{" +
                                $"\"string\":\"{to}\"" +
                            "}," +
                            "{" +
                                $"\"int\":\"{amount}\"" +
                            "}" +
                        "]" +
                    "}" +
                "]" +
            "}";
        }
    }
}