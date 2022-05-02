using System.Collections.Generic;
using System.Linq;

using Atomex.Common;

namespace Atomex.MarketData.Entities
{
    public class Entry
    {
        public long TransactionId { get; set; }
        public string Symbol { get; set; }
        public Side Side { get; set; }
        public decimal Price { get; set; }
        public IList<decimal> QtyProfile { get; set; }

        public Entry()
        {
            QtyProfile = new List<decimal>();
        }

        public decimal Qty() => QtyProfile?.Sum() ?? 0;
    }
}