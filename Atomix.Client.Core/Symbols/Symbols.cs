using System.Linq;
using Atomix.Core.Entities;

namespace Atomix
{
    public static class Symbols
    {
        public static Symbol LtcBtc = new LtcBtc();
        public static Symbol EthBtc = new EthBtc();

        public static Symbol[] Available =
        {
            LtcBtc,
            EthBtc
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