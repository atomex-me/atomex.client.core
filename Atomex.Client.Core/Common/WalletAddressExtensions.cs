using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Serilog;

using Atomex.Cryptography.Abstract;
using Atomex.Wallet.Abstract;
using WalletAddress = Atomex.Wallets.WalletAddress;
using DtoWalletAddress = Atomex.Client.V1.Entities.WalletAddress;

namespace Atomex.Common
{
    public static class WalletAddressExtensions
    {
        public static async Task<IEnumerable<DtoWalletAddress>> CreateProofOfPossessionAsync(
            this IEnumerable<WalletAddress> fromWallets,
            DateTime timeStamp,
            IAccount account)
        {
            try
            {
                var result = new List<DtoWalletAddress>();

                foreach (var address in fromWallets)
                {
                    var nonce = Guid.NewGuid().ToString();

                    var data = Encoding.Unicode
                        .GetBytes($"{nonce}{timeStamp.ToUniversalTime():yyyy.MM.dd HH:mm:ss.fff}");

                    var hashToSign = HashAlgorithm.Sha256.Hash(data);

                    var currencyConfig = account.Currencies
                        .GetByName(address.Currency);

                    var signature = await account.Wallet
                        .SignHashAsync(hashToSign, address, currencyConfig)
                        .ConfigureAwait(false);

                    if (signature == null)
                        throw new Exception("Error during creation of proof of possession. Sign is null");

                    var proofOfPossession = Convert.ToBase64String(signature);

                    Log.Verbose("ProofOfPossession: {@signature}", proofOfPossession);

                    var publicKey = account.Wallet.GetPublicKey(
                        currency: currencyConfig,
                        keyPath: address.KeyPath,
                        keyType: address.KeyType);

                    result.Add(new DtoWalletAddress
                    {
                        Address           = address.Address,
                        Currency          = address.Currency,
                        Nonce             = nonce,
                        ProofOfPossession = proofOfPossession,
                        PublicKey         = Convert.ToBase64String(publicKey)
                    });
                }

                return result;
            }
            catch (Exception e)
            {
                Log.Error(e, "Proof of possession creating error");
            }

            return null;
        }

        public static BigInteger AvailableBalance(this WalletAddress walletAddress) => Currencies.IsBitcoinBased(walletAddress.Currency)
            ? walletAddress.Balance + walletAddress.UnconfirmedIncome
            : walletAddress.Balance;
    }
}