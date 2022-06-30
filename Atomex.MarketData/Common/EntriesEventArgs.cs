using System;
using System.Collections.Generic;

using Atomex.MarketData.Entities;

namespace Atomex.MarketData.Common
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