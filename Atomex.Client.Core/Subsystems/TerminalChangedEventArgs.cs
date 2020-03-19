using System;
using Atomex.Subsystems.Abstract;

namespace Atomex.Subsystems
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