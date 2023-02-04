﻿using System;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common.Memory;
using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface IHdWallet
    {
        /// <summary>
        /// Path to wallet
        /// </summary>
        string PathToWallet { get; }

        /// <summary>
        /// Gets wallet's network
        /// </summary>
        Network Network { get; }

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

        WalletAddress GetAddress(
            CurrencyConfig currency,
            string keyPath,
            int keyType);

        SecureBytes GetPublicKey(
            CurrencyConfig currency,
            string keyPath,
            int keyType);

        /// <summary>
        /// Gets public key for service key with <paramref name="index"/>
        /// </summary>
        /// <param name="index">Service key index</param>
        /// <returns>Public key bytes</returns>
        SecureBytes GetServicePublicKey(uint index);

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
            CurrencyConfig currency,
            CancellationToken cancellationToken = default);

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
            CancellationToken cancellationToken = default);

        byte[] GetDeterministicSecret(
            CurrencyConfig currency,
            DateTime timeStamp);
    }
}