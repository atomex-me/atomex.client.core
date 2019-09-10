using System;

namespace Atomex.MarketData
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