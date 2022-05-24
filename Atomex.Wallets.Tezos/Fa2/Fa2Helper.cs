namespace Atomex.Wallets.Tezos.Fa2
{
    public class Fa2Helper
    {
        public static string TransferParameters(
            int tokenId,
            string from,
            string to,
            decimal amount)
        {
            return "{" +
                $"\"prim\":\"Pair\"," +
                $"\"args\":[" +
                    "{" +
                        $"\"string\":\"{from}\"" +
                    "}," +
                    "[" +
                        "{" +
                            $"\"prim\":\"Pair\"," +
                            $"\"args\":[" +
                                "{" +
                                    $"\"string\":\"{to}\"" +
                                "}," +
                                "{" +
                                    $"\"prim\":\"Pair\"," +
                                    $"\"args\":[" +
                                        "{" +
                                            $"\"int\":\"{tokenId}\"" +
                                        "}," +
                                        "{" +
                                            $"\"int\":\"{string.Format("{0:0}", amount)}\"" +
                                        "}" +
                                    "]" +
                                "}" +
                            "]" +
                        "}" +
                    "]" +
                "]" +
            "}";
        }
    }
}