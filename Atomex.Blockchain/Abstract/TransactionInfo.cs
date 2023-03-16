namespace Atomex.Blockchain.Abstract
{
    public class TransactionInfo<T,M>
        where T : ITransaction
        where M : ITransactionMetadata
    {
        public T Tx { get; init; }
        public M Metadata { get; init; }
    }
}