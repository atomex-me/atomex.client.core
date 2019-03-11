using System;
using Atomix.Core.Entities;

namespace Atomix.MarketData
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