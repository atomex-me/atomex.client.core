namespace Atomex.Blockchain.Bitcoin
{
    public record BitcoinTxPoint
    {
        public uint Index { get; init; }
        public string Hash { get; init; }
    }
}