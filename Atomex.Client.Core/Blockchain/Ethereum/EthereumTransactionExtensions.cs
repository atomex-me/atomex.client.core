namespace Atomex.Blockchain.Ethereum
{
    public static class EthereumTransactionExtensions
    {
        private const int InputItemSizeInHex = 64;

        public static bool IsMethodCall(this EthereumTransaction tx, string methodSignatureHash)
        {
            var signature = tx.Input[..(tx.Input.Length % InputItemSizeInHex)];

            return methodSignatureHash.StartsWith(signature);
        }
    }
}