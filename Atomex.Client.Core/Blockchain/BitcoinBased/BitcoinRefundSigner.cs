using NBitcoin;

using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain.BitcoinBased
{
    public class BitcoinRefundSigner : IBitcoinOutputSigner
    {
        public Script CreateSignatureScript(
            byte[] signature,
            byte[] publicKey,
            Script knownRedeemScript)
        {
            return BitcoinBasedSwapTemplate.GenerateHtlcSwapRefundForP2Sh(
                aliceRefundSig: signature,
                aliceRefundPubKey: publicKey,
                redeemScript: knownRedeemScript.ToBytes());
        }

        public int SignatureSize() => 209; // <signature> <pubkey> 1 <redeem_script>
    }
}