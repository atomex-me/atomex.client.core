namespace Atomix.Blockchain.Bitcoin.Own
{
    public interface IBlockchainTransaction
    {
        byte[] GetBytes();
        byte[] GetHash();
        ITxOutput[] GetOutputs();
    }
}