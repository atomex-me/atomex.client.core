namespace Atomex.Blockchain.Bitcoin
{
    public record BitcoinTxPoint
    {
        public uint Index { get; set; }
        public string Hash { get; set; }
    }
}