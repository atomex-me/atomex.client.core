using NBitcoin;

namespace Atomex.Blockchain.Bitcoin.Common
{
    public static class TransactionBuilderExtensions
    {
        public static TransactionBuilder SetDustPrevention(this TransactionBuilder builder, bool value)
        {
            builder.DustPrevention = value;
            return builder;
        }
    }
}