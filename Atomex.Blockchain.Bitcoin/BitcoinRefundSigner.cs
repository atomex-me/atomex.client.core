using NBitcoin;

using Atomex.Blockchain.Bitcoin.Abstract;

namespace Atomex.Blockchain.Bitcoin
{
    public class BitcoinRefundSigner : IBitcoinOutputSigner
    {
        public Script CreateSignatureScript(
            byte[] signature,
            byte[] publicKey,
            Script knownRedeemScript)
        {
            return BitcoinSwapTemplate.CreateHtlcSwapRefundForP2Sh(
                aliceRefundSig: signature,
                aliceRefundPubKey: publicKey,
                redeemScript: knownRedeemScript.ToBytes());
        }

        public int SignatureSize() => 209; // <signature> <pubkey> 1 <redeem_script>
    }
}