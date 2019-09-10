using System;
using Atomex.Core.Entities;

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