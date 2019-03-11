using System;
using System.Collections.Generic;

namespace Atomix.MarketData
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