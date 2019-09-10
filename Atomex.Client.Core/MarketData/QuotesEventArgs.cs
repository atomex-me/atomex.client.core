using System;
using System.Collections.Generic;

namespace Atomex.MarketData
{
    public class QuotesEventArgs : EventArgs
    {
        public IList<Quote> Quotes { get; }

        public QuotesEventArgs(IList<Quote> quotes)
        {
            Quotes = quotes;
        }
    }
}