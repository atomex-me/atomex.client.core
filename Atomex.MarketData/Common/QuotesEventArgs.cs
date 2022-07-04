using System;
using System.Collections.Generic;

using Atomex.MarketData.Entities;

namespace Atomex.MarketData.Common
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