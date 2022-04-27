using System;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common.Memory;
using Atomex.Cryptography.Abstract;

namespace Atomex.Wallets.Tezos
{
    public class TezosWallet<TKey> : Wallet<TKey> where TKey : IKey
    {
        public TezosWallet(SecureBytes privateKey)
            : base(privateKey, signDataType: SignDataType.Plain)
        {
        }

        public override Task<byte[]> SignAsync(
            ReadOnlyMemory<byte> data,
            string keyPath,
            CancellationToken cancellationToken = default)
        {
            return base.SignAsync(
                data: HashAlgorithm.Blake2b.Hash(data.Span),
                keyPath: keyPath,
                cancellationToken: cancellationToken);
        }

        public override Task<bool> VerifyAsync(
            ReadOnlyMemory<byte> data,
            ReadOnlyMemory<byte> signature,
            string keyPath,
            CancellationToken cancellationToken = default)
        {
            return base.VerifyAsync(
                data: HashAlgorithm.Blake2b.Hash(data.Span),
                signature: signature,
                keyPath: keyPath,
                cancellationToken: cancellationToken);
        }
    }
}