using System;
using System.Collections.Generic;
using System.Linq;

using NBitcoin;

namespace Atomex.Blockchain.Bitcoin
{
    public static class BitcoinSwapTemplate
    {
        /// <summary>
        /// Create atomic swap payment script to <paramref name="bobAddress"/> with HTLC refund and secret size check
        /// </summary>
        /// <param name="aliceRefundAddress">Alice refund address</param>
        /// <param name="bobAddress">Bob target address</param>
        /// <param name="lockTimeStamp">Lock TimeStamp for refund</param>
        /// <param name="secretHash">Secret hash</param>
        /// <param name="secretSize">Secret size in bytes</param>
        /// <param name="expectedNetwork">Expected network necessary to get the correct hash addresses</param>
        /// <returns>Atomic swap payment script</returns>
        public static Script CreateHtlcSwapPayment(
            string aliceRefundAddress,
            string bobAddress,
            long lockTimeStamp,
            byte[] secretHash,
            int secretSize,
            Network expectedNetwork)
        {
            // OP_IF
            //    <lockTimeStamp> OP_CHECKLOCKTIMEVERIFY OP_DROP OP_DUP OP_HASH160 <aliceRefundAddress> OP_EQUALVERIFY OP_CHECKSIG
            // OP_ELSE
            //    OP_SIZE <secretSize> OP_EQUALVERIFY OP_HASH256 <secretHash> OP_EQUALVERIFY OP_DUP OP_HASH160 <bobAddress> OP_EQUALVERIFY OP_CHECKSIG
            // OP_ENDIF

            if (aliceRefundAddress == null)
                throw new ArgumentNullException(nameof(aliceRefundAddress));

            if (bobAddress == null)
                throw new ArgumentNullException(nameof(bobAddress));

            if (secretHash == null)
                throw new ArgumentNullException(nameof(secretHash));

            if (secretSize <= 0)
                throw new ArgumentException("Invalid Secret Size", nameof(secretSize));

            var aliceRefundAddressHash = GetAddressHash(aliceRefundAddress, expectedNetwork);
            var bobAddressHash = GetAddressHash(bobAddress, expectedNetwork);

            return new Script(new List<Op>
            {
                // if refund
                OpcodeType.OP_IF,
                Op.GetPushOp(lockTimeStamp),
                OpcodeType.OP_CHECKLOCKTIMEVERIFY,
                OpcodeType.OP_DROP,
                OpcodeType.OP_DUP,
                OpcodeType.OP_HASH160,
                Op.GetPushOp(aliceRefundAddressHash),
                OpcodeType.OP_EQUALVERIFY,
                OpcodeType.OP_CHECKSIG,
                // else redeem
                OpcodeType.OP_ELSE,
                OpcodeType.OP_SIZE,
                Op.GetPushOp(secretSize),
                OpcodeType.OP_EQUALVERIFY,
                OpcodeType.OP_HASH256,
                Op.GetPushOp(secretHash),
                OpcodeType.OP_EQUALVERIFY,
                OpcodeType.OP_DUP,
                OpcodeType.OP_HASH160,
                Op.GetPushOp(bobAddressHash),
                OpcodeType.OP_EQUALVERIFY,
                OpcodeType.OP_CHECKSIG,
                OpcodeType.OP_ENDIF
            });
        }

        public static byte[] GetAddressHash(string address, Network network)
        {
            var btcAddress = BitcoinAddress.Create(address, network);

            return btcAddress switch
            {
                BitcoinPubKeyAddress pubKeyAddress => pubKeyAddress.Hash.ToBytes(),
                BitcoinWitPubKeyAddress witPubKeyAddress => witPubKeyAddress.Hash.ToBytes(),
                BitcoinScriptAddress scriptAddress => scriptAddress.Hash.ToBytes(),
                BitcoinWitScriptAddress witScriptAddress => witScriptAddress.Hash.ToBytes(),
                _ => throw new NotSupportedException("Not supported btc address type")
            };
        }

        /// <summary>
        /// Create atomic swap refund script for swap scheme with HTLC
        /// </summary>
        /// <param name="aliceRefundSig">Alice signature</param>
        /// <param name="aliceRefundPubKey">Alice refund public key</param>
        /// <param name="redeemScript">Redeem script</param>
        /// <returns>Atomic swap refund script</returns>
        public static Script CreateSwapRefundScript(
            byte[] aliceRefundSig,
            byte[] aliceRefundPubKey,
            byte[] redeemScript)
        {
            // <aliceRefundSig> <aliceRefundPubKey> 1 <redeemScript>

            if (aliceRefundSig == null)
                throw new ArgumentNullException(nameof(aliceRefundSig));

            if (aliceRefundPubKey == null)
                throw new ArgumentNullException(nameof(aliceRefundPubKey));

            return new Script(new List<Op>
            {
                Op.GetPushOp(aliceRefundSig),
                Op.GetPushOp(aliceRefundPubKey),
                Op.GetPushOp(1),
                Op.GetPushOp(redeemScript)
            });
        }

        /// <summary>
        /// Create atomic swap redeem script with secret size control
        /// </summary>
        /// <param name="sig">Bob signature</param>
        /// <param name="pubKey">Bob public key</param>
        /// <param name="secret">Secret</param>
        /// <param name="redeemScript">Redeem script from swap payment tx</param>
        /// <returns>Atomic swap redeem script</returns>
        public static Script CreateSwapRedeemScript(
            byte[] sig,
            byte[] pubKey,
            byte[] secret,
            byte[] redeemScript)
        {
            // <sig> <pubKey> <secret> 0 <redeemScript>

            if (sig == null)
                throw new ArgumentNullException(nameof(sig));

            if (pubKey == null)
                throw new ArgumentNullException(nameof(pubKey));

            if (secret == null)
                throw new ArgumentNullException(nameof(secret));

            return new Script(new List<Op>
            {
                Op.GetPushOp(sig),
                Op.GetPushOp(pubKey),
                Op.GetPushOp(secret),
                Op.GetPushOp(0),
                Op.GetPushOp(redeemScript)
            });
        }

        /// <summary>
        /// Check if the <paramref name="script"/> is HTLC atomic swap payment script
        /// </summary>
        /// <param name="script">Script</param>
        /// <returns>True if <paramref name="script"/> is HTCL atomic swap payment script, otherwise false</returns>
        public static bool IsSwapPayment(Script script)
        {
            try
            {
                var ops = script
                    .ToOps()
                    .ToList();

                if (ops.Count != 22)
                    return false;

                return ops[0].Code == OpcodeType.OP_IF &&
                       ops[2].Code == OpcodeType.OP_CHECKLOCKTIMEVERIFY &&
                       ops[3].Code == OpcodeType.OP_DROP &&
                       ops[4].Code == OpcodeType.OP_DUP &&
                       ops[5].Code == OpcodeType.OP_HASH160 &&
                       ops[7].Code == OpcodeType.OP_EQUALVERIFY &&
                       ops[8].Code == OpcodeType.OP_CHECKSIG &&
                       ops[9].Code == OpcodeType.OP_ELSE &&
                       ops[10].Code == OpcodeType.OP_SIZE &&
                       ops[12].Code == OpcodeType.OP_EQUALVERIFY &&
                       IsSwapHash(ops[13].Code) &&
                       ops[15].Code == OpcodeType.OP_EQUALVERIFY &&
                       ops[16].Code == OpcodeType.OP_DUP &&
                       ops[17].Code == OpcodeType.OP_HASH160 &&
                       ops[19].Code == OpcodeType.OP_EQUALVERIFY &&
                       ops[20].Code == OpcodeType.OP_CHECKSIG &&
                       ops[21].Code == OpcodeType.OP_ENDIF;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsSwapHash(OpcodeType opcodeType) =>
            opcodeType == OpcodeType.OP_HASH160 ||
            opcodeType == OpcodeType.OP_HASH256 ||
            opcodeType == OpcodeType.OP_SHA256;

        /// <summary>
        /// Check if the <paramref name="script"/> is atomic swap redeem script
        /// </summary>
        /// <param name="script"></param>
        /// <returns></returns>
        public static bool IsSwapRedeem(Script script)
        {
            var ops = script
                .ToOps()
                .ToList();

            if (ops.Count < 5)
                return false;

            return ops[^2].Code == OpcodeType.OP_FALSE;
        }

        /// <summary>
        /// Check if <paramref name="script"/> is atomic swap refund script
        /// </summary>
        /// <param name="script">Script</param>
        /// <returns>True if <paramref name="script"/> is atomic swap refund script, else false</returns>
        public static bool IsSwapRefund(Script script)
        {
            var ops = script
                .ToOps()
                .ToList();

            if (ops.Count < 4)
                return false;

            return ops[^2].Code == OpcodeType.OP_TRUE;
        }

        public static IEnumerable<byte[]> ExtractAllPushData(Script script) => script
            .ToOps()
            .Where(op => op.PushData != null)
            .Select(op => op.PushData)
            .ToList();
    }
}