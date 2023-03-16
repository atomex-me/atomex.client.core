using NBitcoin;

namespace Atomex.Blockchain.Bitcoin.Abstract
{
    public interface IBitcoinOutputSigner
    {
        Script CreateSignatureScript(
            byte[] signature,
            byte[] publicKey,
            Script knownRedeemScript);

        int SignatureSize();
    }
}