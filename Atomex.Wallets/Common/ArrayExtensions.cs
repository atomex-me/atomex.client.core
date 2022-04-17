using System;

namespace Atomex.Common
{
    public static class ArrayExtensions
    {
        public static void Clear<T>(this T[] array)
        {
            if (array != null)
                Array.Clear(array: array, index: 0, length: array.Length);
        }
    }
}