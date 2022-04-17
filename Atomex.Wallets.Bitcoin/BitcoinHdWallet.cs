using System.Security;

using NBitcoin;

using Atomex.Common.Memory;

namespace Atomex.Wallets.Bitcoin
{
    public class BitcoinHdWallet : HdWallet<BitcoinExtKey>
    {
        public BitcoinHdWallet(SecureBytes seed)
            : base(seed)
        {
        }

        public BitcoinHdWallet(
            SecureString mnemonic,
            Wordlist wordList,
            SecureString passPhrase)
            : base(mnemonic, wordList, passPhrase)
        {
        }
    }
}