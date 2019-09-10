using System;
using System.Collections.Generic;

namespace Atomix.Common
{
    public class EqualityComparer<T> : IEqualityComparer<T>
    {
        private Func<T, T, bool> Cmp { get; }

        public EqualityComparer(
            Func<T, T, bool> cmp)
        {
            Cmp = cmp;
        }
        public bool Equals(
            T x,
            T y)
        {
            return Cmp(x, y);
        }

        public int GetHashCode(
            T obj)
        {
            return obj.GetHashCode();
        }
    }
}