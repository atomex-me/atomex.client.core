using System;
using Atomex.Core;

namespace Atomex.MarketData
{
    public class MarketDataEventArgs : EventArgs
    {
        public Symbol Symbol { get; }

        public MarketDataEventArgs(Symbol symbol)
        {
            Symbol = symbol;
        }
    }
}