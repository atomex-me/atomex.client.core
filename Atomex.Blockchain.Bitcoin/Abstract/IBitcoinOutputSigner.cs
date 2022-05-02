using NBitcoin;

using Atomex.Common.Memory;

namespace Atomex.Blockchain.Bitcoin.Abstract
{
    public interface IBitcoinOutputSigner
    {
        Script CreateSignatureScript(
            byte[] signature,
            SecureBytes publicKey,
            Script knownRedeemScript);

        int SignatureSize();
    }
}