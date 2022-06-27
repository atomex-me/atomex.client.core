using System;

using Atomex.MarketData.Entities;

namespace Atomex.MarketData.Common
{
    public class SnapshotEventArgs : EventArgs
    {
        public Snapshot Snapshot { get; }

        public SnapshotEventArgs(Snapshot snapshot)
        {
            Snapshot = snapshot;
        }
    }
}