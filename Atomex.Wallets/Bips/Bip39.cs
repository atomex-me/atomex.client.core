using System.Security;
using NBitcoin;

using Atomex.Common;
using Atomex.Common.Memory;

namespace Atomex.Wallets.Bips
{
    public static class Bip39
    {
        public static SecureBytes SeedFromMnemonic(
            SecureString mnemonic,
            Wordlist wordList,
            SecureString passPhrase)
        {
            // todo: use secure memory Bip39 seed generator instead of NBitcoin
            var unsecureMnemonic = mnemonic.ToUnsecuredString();
            var unsecurePassPhrase = passPhrase.ToUnsecuredString();
            var unsecureSeed = new Mnemonic(unsecureMnemonic, wordList)
                .DeriveSeed(unsecurePassPhrase);

            return new SecureBytes(unsecureSeed);
        }
    }
}