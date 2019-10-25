using System;
using Atomex.Subsystems.Abstract;

namespace Atomex.Subsystems
{
    public class TerminalChangedEventArgs : EventArgs
    {
        public ITerminal Terminal { get; }

        public TerminalChangedEventArgs(ITerminal terminal)
        {
            Terminal = terminal;
        }
    }
}