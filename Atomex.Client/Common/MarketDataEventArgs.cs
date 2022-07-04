using System;

namespace Atomex.Client.Common
{
    public class MarketDataEventArgs : EventArgs
    {
        public string Symbol { get; }

        public MarketDataEventArgs(string symbol)
        {
            Symbol = symbol;
        }
    }
}