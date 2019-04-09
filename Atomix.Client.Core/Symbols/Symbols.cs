using System.Linq;
using Atomix.Core.Entities;

namespace Atomix
{
    public static class Symbols
    {
        public static LtcBtc LtcBtc = new LtcBtc();
        public static EthBtc EthBtc = new EthBtc();
        public static XtzBtc XtzBtc = new XtzBtc();
        public static XtzEth XtzEth = new XtzEth();

        public static Symbol[] Available =
        {
            LtcBtc,
            EthBtc,
            XtzBtc,
            XtzEth
        };

        public static Symbol SymbolByCurrencies(Currency from, Currency to)
        {
            if (from == null || to == null)
                return null;

            return Available.FirstOrDefault(s =>
                s.Name.Equals($"{from.Name}/{to.Name}") || s.Name.Equals($"{to.Name}/{from.Name}"));
        }
    }
}