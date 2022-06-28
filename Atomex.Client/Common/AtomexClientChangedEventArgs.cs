using System;

using Atomex.Client.Abstract;

namespace Atomex.Client.Common
{
    public class AtomexClientChangedEventArgs : EventArgs
    {
        public IAtomexClient OldAtomexClient { get; }
        public IAtomexClient AtomexClient { get; }

        public AtomexClientChangedEventArgs(IAtomexClient oldClient, IAtomexClient newClient)
        {
            OldAtomexClient = oldClient;
            AtomexClient = newClient;
        }
    }
}