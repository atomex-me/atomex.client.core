using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace Atomix.Cryptography
{
    public class Ecdsa : IAsymmetricCipher, IAsymmetricSigner
    {
        public string Curve { get; set; } = Curves.Secp256K1;
        public string Algorithm { get; set; } = Identifiers.Sha256WithEcdsa;

        public bool VerifySign(byte[] sign, byte[] data, byte[] publicKey)
        {
            var curve = SecNamedCurves.GetByName(Curve);
            var parameters = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H, curve.GetSeed());
            var key = new ECPublicKeyParameters(Identifiers.Ec, curve.Curve.DecodePoint(publicKey), parameters);    
            
            var signer = SignerUtilities.GetSigner(Algorithm);
            signer.Init(false, key);
            signer.BlockUpdate(data, 0, data.Length);
            return signer.VerifySignature(sign);
        }

        public byte[] Sign(byte[] data, byte[] privateKey)
        {
            var curve = SecNamedCurves.GetByName(Curve);
            var parameters = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H, curve.GetSeed());
            var key = new ECPrivateKeyParameters(Identifiers.Ec, new BigInteger(privateKey), parameters);

            var signer = SignerUtilities.GetSigner(Algorithm);
            signer.Init(true, key);
            signer.BlockUpdate(data, 0, data.Length);
            return signer.GenerateSignature();
        }

        public byte[] Sign(AsymmetricKeyParameter privateKey, byte[] data)
        {
            return Sign(((ECPrivateKeyParameters)privateKey).D.ToByteArray(), data);
        }

        public AsymmetricCipherKeyPair GenerateKeyPair()
        {
            var curve = SecNamedCurves.GetByName(Curve);
            var parameters = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H, curve.GetSeed());

            var generator = new ECKeyPairGenerator(Identifiers.Ecdsa);
            generator.Init(new ECKeyGenerationParameters(parameters, new SecureRandom()));
            return generator.GenerateKeyPair();
        }
    }
}