using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallets
{
    public abstract class Wallet
    {
        public const string SingleKeyPath = "m";
    }

    /// <summary>
    /// Represents an IWallet interface implementation for regular wallets (non-hardwallets)
    /// </summary>
    /// <typeparam name="TKey">Key type</typeparam>
    /// <inheritdoc/>
    public class Wallet<TKey> : Wallet, IWallet where TKey : IKey
    {
        protected static readonly ConstructorDelegate DefaultConstructor =
            Constructor.CreateConstructor(typeof(TKey), typeof(SecureBytes));

        protected SecureBytes _seed;
        protected Func<SecureBytes> _seedCreator;
        protected Func<SecureBytes, TKey> _keyCreator;

        protected Wallet()
        {
        }

        public Wallet(SecureBytes seed, Func<SecureBytes, TKey> keyCreator = null)
        {
            _seed = seed?.Copy() ?? throw new ArgumentNullException(nameof(seed));
            _keyCreator = keyCreator ?? new Func<SecureBytes, TKey>((s) => (TKey)DefaultConstructor(s));
        }

        public Wallet(Func<SecureBytes> seedCreator, Func<SecureBytes, TKey> keyCreator = null)
        {
            _seedCreator = seedCreator ?? throw new ArgumentNullException(nameof(seedCreator));
            _keyCreator = keyCreator ?? new Func<SecureBytes, TKey>((s) => (TKey)DefaultConstructor(s));
        }

        public virtual Task<SecureBytes> GetPublicKeyAsync(
            string keyPath,
            CancellationToken cancellationToken = default)
        {
            if (keyPath != SingleKeyPath)
                throw new ArgumentException("Single key wallet can't derive keys and must use default key path: 'm'");

            using var key = CreateKey(keyPath);

            return Task.FromResult(key.GetPublicKey());
        }

        public virtual async Task<byte[]> SignHashAsync(
            ReadOnlyMemory<byte> hash,
            string keyPath,
            CancellationToken cancellationToken = default)
        {
            if (keyPath != SingleKeyPath)
                throw new ArgumentException("Single key wallet can't derive keys and must use default key path: 'm'.");

            using var key = CreateKey(keyPath);

            return await key
                .SignHashAsync(hash, cancellationToken)
                .ConfigureAwait(false);
        }

        public virtual async Task<IList<byte[]>> SignHashAsync(
            IList<ReadOnlyMemory<byte>> hashes,
            IList<string> keyPathes,
            CancellationToken cancellationToken = default)
        {
            if (hashes.Count != keyPathes.Count)
                return null;

            foreach (var keyPath in keyPathes)
                if (keyPath != SingleKeyPath)
                    throw new ArgumentException("Single key wallet can't derive keys and must use default key path: 'm'.");

            var signatures = new List<byte[]>(hashes.Count);

            for (var i = 0; i < hashes.Count; ++i)
            {
                var signature = await SignHashAsync(
                        hash: hashes[i],
                        keyPath: keyPathes[i],
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                signatures.Add(signature);
            }

            return signatures;
        }

        public virtual async Task<bool> VerifyHashAsync(
            ReadOnlyMemory<byte> hash,
            ReadOnlyMemory<byte> signature,
            string keyPath,
            CancellationToken cancellationToken = default)
        {
            if (keyPath != SingleKeyPath)
                throw new ArgumentException("Single key wallet can't derive keys and must use default key path: 'm'.");

            using var key = CreateKey(keyPath);

            return await key
                .VerifyHashAsync(hash, signature, cancellationToken)
                .ConfigureAwait(false);
        }

        protected virtual IKey CreateKey(string keyPath)
        {
            using var seed = _seed != null
                ? _seed.Copy()
                : _seedCreator();

            return _keyCreator(seed);
        }

        #region IDisposable Support

        private bool disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                    _seed?.Dispose();

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }
}