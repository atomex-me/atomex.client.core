using NBitcoin;

using Atomex.Blockchain.Bitcoin.Abstract;

namespace Atomex.Blockchain.Bitcoin
{
    public class BitcoinRedeemSigner : IBitcoinOutputSigner
    {
        private readonly byte[] _secret;

        public BitcoinRedeemSigner(byte[] secret)
        {
            _secret = secret;
        }

        public Script CreateSignatureScript(
            byte[] signature,
            byte[] publicKey,
            Script knownRedeemScript)
        {
            return BitcoinSwapTemplate.CreateP2PkhSwapRedeemForP2Sh(
                sig: signature,
                pubKey: publicKey,
                secret: _secret,
                redeemScript: knownRedeemScript.ToBytes());
        }

        public int SignatureSize() => 241; // <sig> <pubKey> <secret> 0 <redeemScript>
    }
}