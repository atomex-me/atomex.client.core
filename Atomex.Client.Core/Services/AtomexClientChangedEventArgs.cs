using System;

using Atomex.Services.Abstract;

namespace Atomex.Services
{
    public class AtomexClientChangedEventArgs : EventArgs
    {
        public IAtomexClient AtomexClient { get; }

        public AtomexClientChangedEventArgs(IAtomexClient atomexClient)
        {
            AtomexClient = atomexClient;
        }
    }
}