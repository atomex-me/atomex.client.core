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

                var publicKey = account.Wallet.GetServicePublicKey(keyIndex);

                var signature = await account.Wallet
                    .SignByServiceKeyAsync(data, keyIndex)
                    .ConfigureAwait(false);

                return (publicKey, signature);
            });
    }
}