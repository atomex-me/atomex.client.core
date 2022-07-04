using System;
using System.Collections.Generic;

namespace Atomex.MarketData.Common
{
    public class DescendingComparer<T> : IComparer<T> where T : IComparable<T>
    {
        public int Compare(T x, T y) => y.CompareTo(x);
    }
}