using System;

using Atomex.Services.Abstract;

namespace Atomex.Services
{
    public class TerminalChangedEventArgs : EventArgs
    {
        public IAtomexClient Terminal { get; }

        public TerminalChangedEventArgs(IAtomexClient terminal)
        {
            Terminal = terminal;
        }
    }
}