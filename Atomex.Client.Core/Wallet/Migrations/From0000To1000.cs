using System.IO;
using System.Security;
using System.Text;
using Atomex.Common;
using Atomex.Core;
using Atomex.Cryptography;
using Atomex.Wallet.Bip;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Atomex.Wallet.Migrations
{
    public static class From0000To1000
    {
        public static object LoadFromFile(
            string pathToFile,
            SecureString password)
        {
            var passwordHash = SessionPasswordHelper.GetSessionPasswordBytes(password, hashIterationsCount: 5);
            var encryptedBytes = File.ReadAllBytes(pathToFile);

            var decryptedBytes = Aes.Decrypt(
                encryptedBytes: encryptedBytes,
                keyBytes: passwordHash);

            var json = Encoding.UTF8.GetString(decryptedBytes);

            var storage = JsonConvert.DeserializeObject<JObject>(json);

            var encryptedSeed = storage["Keys"]["1729"]["EncryptedSeed"].ToString();

            var seedPasswordHash = SessionPasswordHelper.GetSessionPasswordBytes(password);

            var seed = Aes.Decrypt(
                encryptedBytes: Hex.FromString(encryptedSeed),
                keyBytes: seedPasswordHash);

            storage["Seed"] = seed.ToHexString();

            return storage;
        }

        public static object Up(
            object oldStorage)
        {
            var jsonOldStorage = (JObject)oldStorage;
            var seed = Hex.FromString(jsonOldStorage["Seed"].ToString());

            var storage = new HdKeyStorage(
                seed: seed,
                network: Network.TestNet);

            storage.NonHdKeys.Add(new NonHdKey
            {
                CurrencyCode = Bip44.Tezos,
                Seed = seed
            });

            return storage;
        }
    }
}