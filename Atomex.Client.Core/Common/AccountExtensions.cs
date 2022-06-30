using Atomex.Client.Common;
using Atomex.Wallet.Abstract;

namespace Atomex.Common
{
    public static class AccountExtensions
    {
        public static AuthMessageSigner DefaultAuthMessageSigner(this IAccount account) =>
            new(async (data, algorithm) =>
            {
                const int keyIndex = 0;

                var securePublicKey = account.Wallet.GetServicePublicKey(keyIndex);
                var publicKey = securePublicKey.ToUnsecuredBytes();

                var signature = await account.Wallet
                    .SignByServiceKeyAsync(data, keyIndex)
                    .ConfigureAwait(false);

                return (publicKey, signature);
            });
    }
}