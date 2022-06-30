namespace Atomex.Client.V1.Entities
{
    public class WalletAddress
    {
        public string Currency { get; set; }
        public string Address { get; set; }
        public string PublicKey { get; set; }
        public string ProofOfPossession { get; set; }
        public string Nonce { get; set; }
    }
}