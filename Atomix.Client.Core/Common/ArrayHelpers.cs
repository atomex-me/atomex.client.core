using System;
using System.Linq;

namespace Atomix.Common
{
    public static class ArrayHelpers
    {
        public static T[] Copy<T>(this T[] arr, int offset, int count)
        {
            var result = new T[count];
            Buffer.BlockCopy(arr, offset, result, 0, count);

            return result;
        }

        public static T[] ConcatArrays<T>(params T[][] arrays)
        {
            var result = new T[arrays.Sum(a => a.Length)];
            var offset = 0;

            foreach (var a in arrays)
            {
                Buffer.BlockCopy(a, 0, result, offset, a.Length);
                offset += a.Length;
            }

            return result;
        }

        public static T[] ConcatArrays<T>(this T[] arr1, T[] arr2)
        {
            var result = new T[arr1.Length + arr2.Length];
            Buffer.BlockCopy(arr1, 0, result, 0, arr1.Length);
            Buffer.BlockCopy(arr2, 0, result, arr1.Length, arr2.Length);

            return result;
        }

        public static T[] ConcatArrays<T>(this T[] array1, T[] array2, int array2Offset, int array2Length)
        {
            var result = new T[array1.Length + array2Length];
            Buffer.BlockCopy(array1, 0, result, 0, array1.Length);
            Buffer.BlockCopy(array2, array2Offset, result, array1.Length, array2Length);
            return result;
        }

        public static T[] SubArray<T>(this T[] arr, int start, int length)
        {
            var result = new T[length];
            Buffer.BlockCopy(arr, start, result, 0, length);

            return result;
        }

        public static T[] SubArray<T>(this T[] arr, int start)
        {
            return SubArray(arr, start, arr.Length - start);
        }
    }
}