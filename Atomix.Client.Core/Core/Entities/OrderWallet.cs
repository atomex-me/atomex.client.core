namespace Atomix.Core.Entities
{
    public class OrderWallet
    {
        public long OrderId { get; set; }
        public Order Order { get; set; }

        public long WalletId { get; set; }
        public WalletAddress Wallet { get; set; }
    }
}