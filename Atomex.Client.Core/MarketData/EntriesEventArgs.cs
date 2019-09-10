using System;
using System.Collections.Generic;

namespace Atomex.MarketData
{
    public class EntriesEventArgs : EventArgs
    {
        public IList<Entry> Entries { get; }

        public EntriesEventArgs(IList<Entry> entries)
        {
            Entries = entries;
        }
    }
}