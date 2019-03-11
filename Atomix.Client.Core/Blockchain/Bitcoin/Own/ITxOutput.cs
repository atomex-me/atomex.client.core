namespace Atomix.Blockchain.Bitcoin.Own
{
    public interface ITxOutput
    {
        long Value { get; set; }
        byte[] ScriptPubKey { get; set; }
        uint Index { get; set; }
        byte[] Hash { get; set; }
    }
}