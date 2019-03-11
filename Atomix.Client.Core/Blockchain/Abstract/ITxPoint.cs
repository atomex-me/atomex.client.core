namespace Atomix.Blockchain.Abstract
{
    public interface ITxPoint
    {
        uint Index { get; }
        string Hash { get; }
    }
}