using System;
using System.Collections.Generic;

namespace Atomex.Common
{
    public static class ListExtensions
    {
        public static List<T> AddEx<T>(this List<T> list, T value)
        {
            list.Add(value);
            return list;
        }

        public static List<T> AddRangeEx<T>(this List<T> list, IEnumerable<T> range)
        {
            if (range != null)
                list.AddRange(range);

            return list;
        }

        public static IList<T> ForEachDo<T>(this IList<T> list, Action<T> action)
        {
            //list.ForEach(action);

            foreach (var item in list)
                action(item);

            return list;
        }

        public static List<T> SortList<T>(this List<T> list, Comparison<T> comparison)
        {
            list.Sort(comparison);
            return list;
        }
    }
}