namespace Atomex.Wallets
{
    public enum WalletType
    {
        PrivateKey,
        Bip32,
        Bip32Ed25519,
        Bip44,
        Bip44Ed25519,
        Bip49,
        Bip84,
        Bip141,
        Ledger,
        Trezor
    }

    /// <summary>
    /// Represents basic wallet information
    /// </summary>
    public class WalletInfo
    {
        /// <summary>
        /// Unique Id
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// Alias name
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Currency
        /// </summary>
        public string Currency { get; set; }
        /// <summary>
        /// Type
        /// </summary>
        public WalletType Type { get; set; }
        /// <summary>
        /// Additional specific type
        /// </summary>
        public int AdditionalType { get; set; }
        /// <summary>
        /// Balance
        /// </summary>
        public Balance Balance { get; set; }
        /// <summary>
        /// Key path pattern
        /// </summary>
        public string KeyPathPattern { get; set; }
        /// <summary>
        /// Is default wallet flag
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// Is Hardware wallet flag
        /// </summary>
        public bool IsHardwareWallet =>
            Type == WalletType.Ledger ||
            Type == WalletType.Trezor;

        /// <summary>
        /// Is single key wallet flag
        /// </summary>
        public bool IsSingleKeyWallet =>
            Type == WalletType.PrivateKey;
    }
}