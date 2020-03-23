using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace Atomex.Cryptography
{
    public class Ecdsa
    {
        public static byte[] Sign(byte[] data, byte[] privateKey, string curveName, string algorithm)
        {
            var curve = SecNamedCurves.GetByName(curveName);
            var parameters = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H, curve.GetSeed());
            var key = new ECPrivateKeyParameters(Algorithms.Ec, new BigInteger(privateKey), parameters);

            var signer = SignerUtilities.GetSigner(algorithm);
            signer.Init(true, key);
            signer.BlockUpdate(data, 0, data.Length);
            return signer.GenerateSignature();
        }

        public static bool Verify(byte[] data, byte[] sign, byte[] publicKey, string curveName, string algorithm)
        {
            var curve = SecNamedCurves.GetByName(curveName);
            var parameters = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H, curve.GetSeed());
            var key = new ECPublicKeyParameters(Algorithms.Ec, curve.Curve.DecodePoint(publicKey), parameters);

            var signer = SignerUtilities.GetSigner(algorithm);
            signer.Init(false, key);
            signer.BlockUpdate(data, 0, data.Length);
            return signer.VerifySignature(sign);
        }
    }
}