using System.Collections.Generic;

namespace Atomex.MarketData.Entities
{
    public class Snapshot
    {
        public long LastTransactionId { get; set; }
        public string Symbol { get; set; }
        public List<Entry> Entries { get; set; }

        public Snapshot()
        {
            Entries = new List<Entry>();
        }
    }
}