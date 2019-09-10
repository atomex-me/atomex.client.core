using System.Collections.Generic;
using System.Linq;
using Atomix.Core;

namespace Atomix.MarketData
{
    public class Entry
    {
        public long TransactionId { get; set; }
        public int SymbolId { get; set; }
        public Side Side { get; set; }
        public decimal Price { get; set; }
        public IList<decimal> QtyProfile { get; set; }
        public Entry()
        {
            QtyProfile = new List<decimal>();
        }
        public decimal Qty()
        {
            return QtyProfile?.Sum() ?? 0;
        }
    }
}