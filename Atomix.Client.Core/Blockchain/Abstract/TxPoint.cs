namespace Atomix.Blockchain.Abstract
{
    public class TxPoint : ITxPoint
    {
        public uint Index { get; }
        public string Hash { get; }

        public TxPoint(uint index, string hash)
        {
            Index = index;
            Hash = hash;
        }
    }
}