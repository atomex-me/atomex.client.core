namespace Atomex.Wallet.Bip
{
    // https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki
    // https://github.com/satoshilabs/slips/blob/master/slip-0044.md
    //
    // Structure:
    // m / purpose' / coin_type' / account' / chain / address_index

    public static class Bip44
    {
        public const int Purpose = 44;

        public const int External = 0;
        public const int Internal = 1;

        public const uint Bitcoin = 0;
        public const uint Testnet = 1;
        public const uint Litecoin = 2;
        public const uint Dogecoin = 3;
        public const uint Dash = 5;
        public const uint Ethereum = 60;
        public const uint EthereumClassic = 61;
        public const uint Monero = 128;
        public const uint Zcash = 133;
        public const uint Ripple = 144;
        public const uint Tezos = 1729;
        public const uint Cardano = 1815;
    }
}