using System.Collections.Generic;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Core.Entities;

namespace Atomix.Wallet.Abstract
{
    public interface IHdWallet
    {
        /// <summary>
        /// Path to wallet
        /// </summary>
        string PathToWallet { get; set; }

        /// <summary>
        /// Gets all currencies supported by the wallet
        /// </summary>
        IEnumerable<Currency> Currencies { get; }

        /// <summary>
        /// Gets the value that indicates whether the wallet is locked
        /// </summary>
        bool IsLocked { get; }

        /// <summary>
        /// Lock wallet
        /// </summary>
        void Lock();

        /// <summary>
        /// Unlock wallet
        /// </summary>
        /// <param name="password">Password</param>
        void Unlock(SecureString password);

        /// <summary>
        /// Encrypt wallet keys by <paramref name="password"/>
        /// </summary>
        /// <param name="password">Password</param>
        /// <returns>Encryption task</returns>
        Task EncryptAsync(SecureString password);

        /// <summary>
        /// Gets address for <paramref name="currency"/>, <paramref name="chain"/> and key <paramref name="index"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="chain">Chain</param>
        /// <param name="index">Key index</param>
        /// <returns>Address</returns>
        WalletAddress GetAddress(
            Currency currency,
            uint chain,
            uint index);

        /// <summary>
        /// Gets wallet address for <paramref name="address"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="address">Address</param>
        /// <param name="maxIndex">Maximum search index</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Wallet address</returns>
        Task<WalletAddress> GetAddressAsync(
            Currency currency,
            string address,
            uint maxIndex,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Gets public key for service key with <paramref name="index"/>
        /// </summary>
        /// <param name="index">Service key index</param>
        /// <returns>Public key bytes</returns>
        byte[] GetServicePublicKey(uint index);

        /// <summary>
        /// Gets internal address for <paramref name="currency"/> and key <paramref name="index"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="index">Key index</param>
        /// <returns>Address</returns>
        WalletAddress GetInternalAddress(
            Currency currency,
            uint index);

        /// <summary>
        /// Gets external address for <paramref name="currency"/> and key <paramref name="index"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="index">Key index</param>
        /// <returns>Address</returns>
        WalletAddress GetExternalAddress(
            Currency currency,
            uint index);

        /// <summary>
        /// Sign <paramref name="data"/> using public key corresponding to <paramref name="address"/>
        /// </summary>
        /// <param name="data">Data to sign</param>
        /// <param name="address">Address</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Signature</returns>
        Task<byte[]> SignAsync(
            byte[] data,
            WalletAddress address,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Sign input/output <paramref name="transaction"/> corresponding to <paramref name="spentOutputs"/>
        /// </summary>
        /// <param name="transaction">In/out transaction to sign</param>
        /// <param name="spentOutputs">Spent outputs</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True, if transaction successfully signed, otherwise else</returns>
        Task<bool> SignAsync(
            IInOutTransaction transaction,
            IEnumerable<ITxOutput> spentOutputs,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Sign address based <paramref name="transaction"/> by key for <paramref name="address"/>
        /// </summary>
        /// <param name="transaction">Address based transaction to sign</param>
        /// <param name="address">Address</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True, if transaction successfully signed, otherwise else</returns>
        Task<bool> SignAsync(
            IAddressBasedTransaction transaction,
            string address,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Sign transaction <paramref name="hash"/> using public key corresponding to <paramref name="address"/>
        /// </summary>
        /// <param name="hash">Transaction hash</param>
        /// <param name="address">Address</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Signature</returns>
        Task<byte[]> SignHashAsync(
            byte[] hash,
            WalletAddress address,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Sign <paramerf name="data"/> using service key with <paramref name="keyIndex"/>
        /// </summary>
        /// <param name="data">Data to sign</param>
        /// <param name="keyIndex">Service key index</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Signature</returns>
        Task<byte[]> SignByServiceKeyAsync(
            byte[] data,
            uint keyIndex,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}