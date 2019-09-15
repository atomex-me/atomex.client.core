using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;

namespace Atomex.Blockchain.BitcoinBased
{
    public static class BitcoinBasedSwapTemplate
    {
        /// <summary>
        /// Generate atomic swap payment script to P2PK <paramref name="bobDestinationPubKey"/> with multisig refund
        /// </summary>
        /// <param name="aliceRefundPubKey">Alice public key for refund</param>
        /// <param name="bobRefundPubKey">Bob public key for refund</param>
        /// <param name="bobDestinationPubKey">Bob public key</param>
        /// <param name="secretHash">Secret hash</param>
        /// <returns>Atomic swap payment script</returns>
        public static Script GenerateP2PkSwapPayment(
            byte[] aliceRefundPubKey,
            byte[] bobRefundPubKey,
            byte[] bobDestinationPubKey,
            byte[] secretHash)
        {
            // OP_IF
            //    2 <aliceRefundPubKey> <bobRefundPubKey> 2 CHECKMULTISIG
            // OP_ELSE
            //    OP_HASH256 <secretHash> OP_EQUALVERIFY <bobDestinationPubKey> OP_CHECKSIG
            // OP_ENDIF

            if (aliceRefundPubKey == null)
                throw new ArgumentNullException(nameof(aliceRefundPubKey));

            if (bobRefundPubKey == null)
                throw new ArgumentNullException(nameof(bobRefundPubKey));

            if (bobDestinationPubKey == null)
                throw new ArgumentNullException(nameof(bobDestinationPubKey));

            if (secretHash == null)
                throw new ArgumentNullException(nameof(secretHash));

            return new Script(new List<Op>
            {
                OpcodeType.OP_IF,
                Op.GetPushOp(2),
                Op.GetPushOp(aliceRefundPubKey),
                Op.GetPushOp(bobRefundPubKey),
                Op.GetPushOp(2),
                OpcodeType.OP_CHECKMULTISIG,
                OpcodeType.OP_ELSE,
                OpcodeType.OP_HASH256,
                Op.GetPushOp(secretHash),
                OpcodeType.OP_EQUALVERIFY,
                Op.GetPushOp(bobDestinationPubKey),
                OpcodeType.OP_CHECKSIG,
                OpcodeType.OP_ENDIF
            });
        }

        /// <summary>
        /// Generate atomic swap payment script to P2PK <paramref name="bobDestinationPubKey"/> with multisig refund
        /// </summary>
        /// <param name="aliceRefundPubKey">Alice public key for refund</param>
        /// <param name="bobRefundPubKey">Bob public key for refund</param>
        /// <param name="bobDestinationPubKey">Bob public key</param>
        /// <param name="secretHash">Secret hash</param>
        /// <returns>Atomic swap payment script</returns>
        public static Script GenerateP2PkSwapPayment(
            PubKey aliceRefundPubKey,
            PubKey bobRefundPubKey,
            PubKey bobDestinationPubKey,
            byte[] secretHash)
        {
            return GenerateP2PkSwapPayment(
                aliceRefundPubKey: aliceRefundPubKey.ToBytes(),
                bobRefundPubKey: bobRefundPubKey.ToBytes(),
                bobDestinationPubKey: bobDestinationPubKey.ToBytes(),
                secretHash: secretHash);
        }

        /// <summary>
        /// Generate atomic swap payment script to P2PKH <paramref name="bobAddress"/> with multisig refund
        /// </summary>
        /// <param name="aliceRefundPubKey">Alice public key for refund</param>
        /// <param name="bobRefundPubKey">Bob public key for refund</param>
        /// <param name="bobAddress">Bob target address</param>
        /// <param name="secretHash">Secret hash</param>
        /// <param name="expectedNetwork">Expected network necessary to get the correct hash address</param>
        /// <returns>Atomic swap payment script</returns>
        public static Script GenerateP2PkhSwapPayment(
            byte[] aliceRefundPubKey,
            byte[] bobRefundPubKey,
            string bobAddress,
            byte[] secretHash,
            Network expectedNetwork = null)
        {
            // OP_IF
            //    2 <aliceRefundPubKey> <bobRefundPubKey> 2 CHECKMULTISIG
            // OP_ELSE
            //    OP_HASH256 <secretHash> OP_EQUALVERIFY OP_DUP OP_HASH160 <bobAddress> OP_EQUALVERIFY OP_CHECKSIG
            // OP_ENDIF

            if (aliceRefundPubKey == null)
                throw new ArgumentNullException(nameof(aliceRefundPubKey));

            if (bobRefundPubKey == null)
                throw new ArgumentNullException(nameof(bobRefundPubKey));

            if (bobAddress == null)
                throw new ArgumentNullException(nameof(bobAddress));

            if (secretHash == null)
                throw new ArgumentNullException(nameof(secretHash));

            var bobAddressHash = new BitcoinPubKeyAddress(bobAddress, expectedNetwork).Hash;

            return new Script(new List<Op>
            {
                OpcodeType.OP_IF,
                Op.GetPushOp(2),
                Op.GetPushOp(aliceRefundPubKey),
                Op.GetPushOp(bobRefundPubKey),
                Op.GetPushOp(2),
                OpcodeType.OP_CHECKMULTISIG,
                OpcodeType.OP_ELSE,
                OpcodeType.OP_HASH256,
                Op.GetPushOp(secretHash),
                OpcodeType.OP_EQUALVERIFY,
                OpcodeType.OP_DUP,
                OpcodeType.OP_HASH160,
                Op.GetPushOp(bobAddressHash.ToBytes()),
                OpcodeType.OP_EQUALVERIFY,
                OpcodeType.OP_CHECKSIG,
                OpcodeType.OP_ENDIF
            });
        }

        /// <summary>
        /// Generate atomic swap payment script to P2PKH <paramref name="bobAddress"/> with multisig refund
        /// </summary>
        /// <param name="aliceRefundPubKey">Alice public key for refund</param>
        /// <param name="bobRefundPubKey">Bob public key for refund</param>
        /// <param name="bobAddress">Bob target address</param>
        /// <param name="secretHash">Secret hash</param>
        /// <param name="expectedNetwork">Expected network necessary to get the correct hash address</param>
        /// <returns>Atomic swap payment script</returns>
        public static Script GenerateP2PkhSwapPayment(
            PubKey aliceRefundPubKey,
            PubKey bobRefundPubKey,
            string bobAddress,
            byte[] secretHash,
            Network expectedNetwork = null)
        {
            return GenerateP2PkhSwapPayment(
                aliceRefundPubKey: aliceRefundPubKey.ToBytes(),
                bobRefundPubKey: bobRefundPubKey.ToBytes(),
                bobAddress: bobAddress,
                secretHash: secretHash,
                expectedNetwork: expectedNetwork);
        }

        /// <summary>
        /// Generate atomic swap payment script to P2PKH <paramref name="bobAddress"/> with HTLC refund and secret size check
        /// </summary>
        /// <param name="aliceRefundAddress">Alice refund address</param>
        /// <param name="bobAddress">Bob target address</param>
        /// <param name="lockTimeStamp">Lock TimeStamp for refund</param>
        /// <param name="secretHash">Secret hash</param>
        /// <param name="secretSize">Secret size in bytes</param>
        /// <param name="expectedNetwork">Expected network necessary to get the correct hash addresses</param>
        /// <returns>Atomic swap payment script</returns>
        public static Script GenerateHtlcP2PkhSwapPayment(
            string aliceRefundAddress,
            string bobAddress,
            long lockTimeStamp,
            byte[] secretHash,
            int secretSize,
            Network expectedNetwork = null)
        {
            // OP_IF
            //    <lockTimeStamp> OP_CHECKLOCKTIMEVERIFY OP_DROP OP_DUP OP_HASH160 <aliceRefundAddress> OP_EQUALVERIFY CHECKSIG
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

            var aliceRefundAddressHash = new BitcoinPubKeyAddress(aliceRefundAddress, expectedNetwork).Hash;
            var bobAddressHash = new BitcoinPubKeyAddress(bobAddress, expectedNetwork).Hash;

            return new Script(new List<Op>
            {
                // if refund
                OpcodeType.OP_IF,
                Op.GetPushOp(lockTimeStamp),
                OpcodeType.OP_CHECKLOCKTIMEVERIFY,
                OpcodeType.OP_DROP,
                OpcodeType.OP_DUP,
                OpcodeType.OP_HASH160,
                Op.GetPushOp(aliceRefundAddressHash.ToBytes()),
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
                Op.GetPushOp(bobAddressHash.ToBytes()),
                OpcodeType.OP_EQUALVERIFY,
                OpcodeType.OP_CHECKSIG,
                OpcodeType.OP_ENDIF
            });
        }

        /// <summary>
        /// Generate atomic swap refund script with Bob signature only
        /// </summary>
        /// <param name="bobRefundSig">Bob signature</param>
        /// <returns>Atomic swap Bob's refund script</returns>
        public static Script GenerateSwapRefundByBob(byte[] bobRefundSig)
        {
            if (bobRefundSig == null)
                throw new ArgumentNullException(nameof(bobRefundSig));

            return new Script(new List<Op>
            {
                Op.GetPushOp(bobRefundSig),
            });
        }

        /// <summary>
        /// Generate atomic swap refund script
        /// </summary>
        /// <param name="aliceRefundSig">Alice signature</param>
        /// <param name="bobRefundSig">Bob signature</param>
        /// <returns>Atomic swap refund script</returns>
        public static Script GenerateSwapRefund(
            byte[] aliceRefundSig,
            byte[] bobRefundSig)
        {
            // OP_0 <aliceRefundSig> <bobRefundSig> 1

            if (aliceRefundSig == null)
                throw new ArgumentNullException(nameof(aliceRefundSig));

            if (bobRefundSig == null)
                throw new ArgumentNullException(nameof(bobRefundSig));

            return new Script(new List<Op>
            {
                Op.GetPushOp(0),
                Op.GetPushOp(aliceRefundSig),
                Op.GetPushOp(bobRefundSig),
                Op.GetPushOp(1)
            });
        }

        /// <summary>
        /// Generate atomic swap refund script
        /// </summary>
        /// <param name="aliceRefundSig">Alice signature</param>
        /// <param name="bobRefundSig">Bob signature</param>
        /// <returns>Atomic swap refund script</returns>
        public static Script GenerateSwapRefund(
            TransactionSignature aliceRefundSig,
            TransactionSignature bobRefundSig)
        {
            return GenerateSwapRefund(aliceRefundSig.ToBytes(), bobRefundSig.ToBytes());
        }

        /// <summary>
        /// Generate atomic swap refund script
        /// </summary>
        /// <param name="aliceRefundSig">Alice signature</param>
        /// <param name="aliceRefundPubKey">Alice refund public key</param>
        /// <returns>Atomic swap refund script</returns>
        public static Script GenerateHtlcSwapRefund(
            byte[] aliceRefundSig,
            byte[] aliceRefundPubKey)
        {
            // <aliceRefundSig> <aliceRefundPubKey> 1

            if (aliceRefundSig == null)
                throw new ArgumentNullException(nameof(aliceRefundSig));

            if (aliceRefundPubKey == null)
                throw new ArgumentNullException(nameof(aliceRefundPubKey));

            return new Script(new List<Op>
            {
                Op.GetPushOp(aliceRefundSig),
                Op.GetPushOp(aliceRefundPubKey),
                Op.GetPushOp(1)
            });
        }

        /// <summary>
        /// Generate atomic swap refund script
        /// </summary>
        /// <param name="aliceRefundSig">Alice signature</param>
        /// <param name="aliceRefundPubKey">Alice refund public key</param>
        /// <returns>Atomic swap refund script</returns>
        public static Script GenerateHtlcSwapRefund(
            TransactionSignature aliceRefundSig,
            byte[] aliceRefundPubKey)
        {
            return GenerateHtlcSwapRefund(aliceRefundSig.ToBytes(), aliceRefundPubKey);
        }

        /// <summary>
        /// Generate atomic swap refund script
        /// </summary>
        /// <param name="aliceRefundSig">Alice signature</param>
        /// <param name="aliceRefundPubKey">Alice refund public key</param>
        /// <param name="redeemScript">Redeem script</param>
        /// <returns>Atomic swap refund script</returns>
        public static Script GenerateHtlcP2PkhP2ShSwapRefund(
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
        /// Generate atomic swap P2PK redeem script
        /// </summary>
        /// <param name="sig">Bob signature</param>
        /// <param name="secret">Secret</param>
        /// <returns>Atomic swap redeem script</returns>
        public static Script GenerateP2PkSwapRedeem(
            byte[] sig,
            byte[] secret)
        {
            // <sig> <secret> 0

            if (sig == null)
                throw new ArgumentNullException(nameof(sig));

            if (secret == null)
                throw new ArgumentNullException(nameof(secret));

            return new Script(new List<Op>
            {
                Op.GetPushOp(sig),
                Op.GetPushOp(secret),
                Op.GetPushOp(0)
            });
        }

        /// <summary>
        /// Generate atomic swap P2PK redeem script
        /// </summary>
        /// <param name="sig">Bob signature</param>
        /// <param name="secret">Secret</param>
        /// <returns>Atomic swap redeem script</returns>
        public static Script GenerateP2PkSwapRedeem(
            TransactionSignature sig,
            byte[] secret)
        {
            return GenerateP2PkSwapRedeem(sig.ToBytes(), secret);
        }

        /// <summary>
        /// Generate atomic swap P2PKH redeem script
        /// </summary>
        /// <param name="sig">Bob signature</param>
        /// <param name="pubKey">Bob public key</param>
        /// <param name="secret">Secret</param>
        /// <returns>Atomic swap redeem script</returns>
        public static Script GenerateP2PkhSwapRedeem(
            byte[] sig,
            byte[] pubKey,
            byte[] secret)
        {
            // <sig> <pubKey> <secret> 0

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
                Op.GetPushOp(0)
            });
        }

        /// <summary>
        /// Generate atomic swap P2PKH redeem script
        /// </summary>
        /// <param name="sig">Bob signature</param>
        /// <param name="pubKey">Bob public key</param>
        /// <param name="secret">Secret</param>
        /// <returns>Atomic swap redeem script</returns>
        public static Script GenerateHtlcP2PkhSwapRedeem(
            byte[] sig,
            byte[] pubKey,
            byte[] secret)
        {
            return GenerateP2PkhSwapRedeem(sig, pubKey, secret);
        }

        /// <summary>
        /// Check if the <paramref name="script"/> is P2PKH atomic swap payment script
        /// </summary>
        /// <param name="script">Script</param>
        /// <returns>True if <paramref name="script"/> is a P2PKH atomic swap payment script, else false</returns>
        public static bool IsP2PkhSwapPayment(Script script)
        {
            var ops = script.ToOps().ToList();

            if (ops.Count != 16)
                return false;

            return ops[0].Code == OpcodeType.OP_IF &&
                   ops[1].Code == OpcodeType.OP_2 &&
                   ops[4].Code == OpcodeType.OP_2 &&
                   ops[5].Code == OpcodeType.OP_CHECKMULTISIG &&
                   ops[6].Code == OpcodeType.OP_ELSE &&
                   IsSwapHash(ops[7].Code) && //ops[7].Code == OpcodeType.OP_SHA256
                   ops[9].Code == OpcodeType.OP_EQUALVERIFY &&
                   ops[10].Code == OpcodeType.OP_DUP &&
                   ops[11].Code == OpcodeType.OP_HASH160 &&
                   ops[13].Code == OpcodeType.OP_EQUALVERIFY &&
                   ops[14].Code == OpcodeType.OP_CHECKSIG &&
                   ops[15].Code == OpcodeType.OP_ENDIF;
        }

        public static bool IsHtlcP2PkhSwapPayment(Script script)
        {
            var ops = script.ToOps().ToList();

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

        public static bool IsSwapHash(OpcodeType opcodeType)
        {
            return opcodeType == OpcodeType.OP_HASH160 ||
                   opcodeType == OpcodeType.OP_HASH256 ||
                   opcodeType == OpcodeType.OP_SHA256;
        }

        /// <summary>
        /// Check if the <paramref name="script"/> is a P2PKH atomic swap redeem script
        /// </summary>
        /// <param name="script">Script</param>
        /// <returns>True if <paramref name="script"/> is a P2PKH atomic swap redeem script, else false</returns>
        public static bool IsP2PkhSwapRedeem(Script script)
        {
            var ops = script.ToOps().ToList();

            if (ops.Count != 4)
                return false;

            return ops[3].Code == OpcodeType.OP_0;
        }

        public static byte[] ExtractSecretFromP2PkhSwapRedeem(Script script)
        {
            var ops = script.ToOps().ToList();

            if (ops.Count != 4)
                throw new ArgumentException("Script is not P2PKH swap redeem", nameof(script));

            return ops[2].PushData;
        }

        public static long ExtractLockTimeFromHtlcP2PkhSwapPayment(Script script)
        {
            var ops = script.ToOps().ToList();

            return ops[1].GetLong() ?? 0;
        }

        public static byte[] ExtractSecretHashFromHtlcP2PkhSwapPayment(Script script)
        {
            var ops = script.ToOps().ToList();

            return ops[14].PushData;
        }

        public static byte[] ExtractTargetPkhFromHtlcP2PkhSwapPayment(Script script)
        {
            var ops = script.ToOps().ToList();

            return ops[18].PushData;
        }

        public static byte[] ExtractSignFromP2PkhSwapRefund(Script script)
        {
            var ops = script.ToOps().ToList();

            if (ops.Count != 1)
                throw new ArgumentException("Script is not P2PKH one side signed swap refund", nameof(script));

            return ops[0].PushData;
        }
    }
}