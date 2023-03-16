using System;
using System.Collections.Generic;

namespace Atomex.Common
{
    public class EqualityComparer<T> : IEqualityComparer<T>
    {
        private Func<T, T, bool> Cmp { get; }
        private Func<T, int> HashCode { get; }

        public EqualityComparer(
            Func<T, T, bool> cmp,
            Func<T, int> hashCode)
        {
            Cmp = cmp;
            HashCode = hashCode;
        }
        public bool Equals(T x, T y) => Cmp(x, y);

        public int GetHashCode(T obj) => HashCode(obj);
    }
}