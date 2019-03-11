using Atomix.Core.Entities;

namespace Atomix
{
    public static class Currencies
    {
        public static Bitcoin Btc = new Bitcoin();
        public static Ethereum Eth = new Ethereum();
        public static Litecoin Ltc = new Litecoin();
        public static Tezos Xtz = new Tezos();

        public static Currency[] Available =
        {
            Btc,
            Eth,
            Ltc,
            Xtz,
        };
    }
}