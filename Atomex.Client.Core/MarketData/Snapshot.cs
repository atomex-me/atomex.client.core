using System.Collections.Generic;

namespace Atomex.MarketData
{
    public class Snapshot
    {
        public long LastTransactionId { get; set; }
        public int SymbolId { get; set; }
        public List<Entry> Entries { get; set; }

        public Snapshot()
        {
            Entries = new List<Entry>();
        }
    }
}