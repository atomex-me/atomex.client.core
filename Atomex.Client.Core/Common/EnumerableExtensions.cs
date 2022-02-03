using System;
using System.Collections.Generic;
using System.Linq;

namespace Atomex.Common
{
    public static class EnumerableExtensions
    {
        public static T MaxBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> selector)
            where TKey : IComparable
        {
            var maxElement = source.First();
            var maxKey = selector(maxElement);

            foreach (var element in source)
            {
                var key = selector(element);
                if (key.CompareTo(maxKey) > 0)
                {
                    maxElement = element;
                    maxKey = key;
                }
            }

            return maxElement;
        }

        public static T MaxByOrDefault<T, TKey>(this IEnumerable<T> source, Func<T, TKey> selector)
            where TKey : IComparable
        {
            return source.Any()
                ? MaxBy(source, selector)
                : default;
        }

        public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source) =>
            source.Where(i => i != null);
    }
}