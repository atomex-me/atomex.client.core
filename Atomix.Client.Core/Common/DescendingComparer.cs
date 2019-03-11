using System;
using System.Collections.Generic;

namespace Atomix.Common
{
    public class DescendingComparer<T> : IComparer<T> where T : IComparable<T>
    {
        public int Compare(T x, T y) => y.CompareTo(x);
    }
}