using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallets
{
    public static class Wallet
    {
        public const string SingleKeyPath = "m";
    }

    /// <summary>
    /// Represents an IWallet interface implementation for regular wallets (non-hardwallets)
    /// </summary>
    /// <typeparam name="TKey">Key type</typeparam>
    /// <inheritdoc/>
    public class Wallet<TKey> : IWallet where TKey : IKey
    {
        public const string SingleKeyPath = "m";

        protected static readonly ConstructorDelegate DefaultConstructor =
            Constructor.CreateConstructor(typeof(TKey), typeof(SecureBytes));

        protected SecureBytes _seed;
        protected Func<SecureBytes> _seedCreator;
        protected Func<SecureBytes, TKey> _keyCreator;

        public SignDataType SignDataType { get; protected set; }

        protected Wallet()
        {
        }

        public Wallet(
            SecureBytes seed,
            Func<SecureBytes, TKey> keyCreator = null,
            SignDataType signDataType = SignDataType.Plain)
        {
            _seed = seed?.Copy() ?? throw new ArgumentNullException(nameof(seed));
            _keyCreator = keyCreator ?? new Func<SecureBytes, TKey>((s) => (TKey)DefaultConstructor(s));
            SignDataType = signDataType;
        }

        public Wallet(
            Func<SecureBytes> seedCreator,
            Func<SecureBytes, TKey> keyCreator = null,
            SignDataType signDataType = SignDataType.Plain)
        {
            _seedCreator = seedCreator ?? throw new ArgumentNullException(nameof(seedCreator));
            _keyCreator = keyCreator ?? new Func<SecureBytes, TKey>((s) => (TKey)DefaultConstructor(s));
            SignDataType = signDataType;
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

        public virtual async Task<byte[]> SignAsync(
            ReadOnlyMemory<byte> data,
            string keyPath,
            CancellationToken cancellationToken = default)
        {
            if (keyPath != SingleKeyPath)
                throw new ArgumentException("Single key wallet can't derive keys and must use default key path: 'm'.");

            using var key = CreateKey(keyPath);

            return await key
                .SignAsync(data, cancellationToken)
                .ConfigureAwait(false);
        }

        public virtual async Task<IList<byte[]>> SignAsync(
            IList<ReadOnlyMemory<byte>> data,
            IList<string> keyPathes,
            CancellationToken cancellationToken = default)
        {
            if (data.Count != keyPathes.Count)
                return null;

            foreach (var keyPath in keyPathes)
                if (keyPath != SingleKeyPath)
                    throw new ArgumentException("Single key wallet can't derive keys and must use default key path: 'm'.");

            var signatures = new List<byte[]>(data.Count);

            for (var i = 0; i < data.Count; ++i)
            {
                var signature = await SignAsync(
                        data: data[i],
                        keyPath: keyPathes[i],
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                signatures.Add(signature);
            }

            return signatures;
        }

        public virtual async Task<bool> VerifyAsync(
            ReadOnlyMemory<byte> data,
            ReadOnlyMemory<byte> signature,
            string keyPath,
            CancellationToken cancellationToken = default)
        {
            if (keyPath != SingleKeyPath)
                throw new ArgumentException("Single key wallet can't derive keys and must use default key path: 'm'.");

            using var key = CreateKey(keyPath);

            return await key
                .VerifyAsync(data, signature, cancellationToken)
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
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}