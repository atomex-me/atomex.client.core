namespace Atomex.Blockchain.Ethereum
{
    public static class EthereumTransactionExtensions
    {
        private const int InputItemSizeInHex = 64;

        public static bool IsMethodCall(this string data, string methodSignatureHash)
        {
            var signature = data[..(data.Length % InputItemSizeInHex)];

            return methodSignatureHash.StartsWith(signature);
        }
    }
}