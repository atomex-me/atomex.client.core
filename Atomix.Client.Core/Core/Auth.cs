using System;
using System.Text;
using Atomix.Common;
using Atomix.Cryptography;
using NBitcoin;

namespace Atomix.Core
{
    public class Auth
    {
        public const double MaxTimeStampDifferenceMs = 3 * 60 * 1000; // 3 minutes
        public const int MinClientNonceLength = 16;

        public DateTime TimeStamp { get; set; }
        public string Nonce { get; set; }
        public string ClientNonce { get; set; }
        public string PublicKeyHex { get; set; }
        public string Signature { get; set; }

        public byte[] PublicKey
        {
            get
            {
                try
                {
                    return PublicKeyHex != null ? Hex.FromString(PublicKeyHex) : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public string UserId()
        {
            return Sha256.Compute(Sha256.Compute(PublicKey)).ToHexString();
        }

        public byte[] SignedData => Encoding.UTF8.GetBytes($"{TimeStamp:u}{Nonce}{ClientNonce}");

        private bool CheckTimeStamp()
        {
            var differenceMs = Math.Abs((DateTime.UtcNow - TimeStamp).TotalMilliseconds);

            return differenceMs <= MaxTimeStampDifferenceMs;
        }

        private bool CheckNonce(string nonce)
        {
            return Nonce != null && Nonce == nonce;
        }

        private bool CheckClientNonce()
        {
            return ClientNonce != null && ClientNonce.Length >= MinClientNonceLength;
        }

        public bool VerifySignature()
        {
            if (Signature == null)
                return false;

            var pubKey = new PubKey(PublicKey);
            return pubKey.VerifyMessage(SignedData, Signature);
        }
        
        public bool Authorize(string nonce, out Error error)
        {
            if (!CheckTimeStamp()) {
                error = new Error(Errors.AuthError, "Invalid timestamp");
                return false;
            }

            if (!CheckNonce(nonce)) {
                error = new Error(Errors.AuthError, "Invalid nonce");
                return false;
            }

            if (!CheckClientNonce()) {
                error = new Error(Errors.AuthError, "Invalid client nonce");
                return false;
            }

            if (!VerifySignature()) {
                error = new Error(Errors.AuthError, "Invalid signature");
                return false;
            }

            error = null;
            return true;
        }
    }
}