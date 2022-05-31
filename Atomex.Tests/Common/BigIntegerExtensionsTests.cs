using System;
using System.Collections.Generic;
using System.Numerics;

using Xunit;

namespace Atomex.Common
{
    public class BigIntegerExtensionsTests
    {
        public static IEnumerable<object[]> Data = new List<object[]>
        {
            new object[] { 1, 0.01m, 2, 2 },
            new object[] { -1, -0.01m, 2, 2 },
            new object[] { 1000, 1m, 3, 3 },
            new object[] { -1000, -1m, 3, 3 },
            new object[] { 1001, 1.001m, 3, 3 },
            new object[] { 123456789, 1.23456789m, 8, 8 },
            new object[] { 1234567891, 1.23456789m, 9, 8 },
            new object[] { BigInteger.Parse("9999999999999999999999999999999999999999"), 99999999.99999999999999999999m, 32, 20 },
            new object[] { 1, 0.0000000000000000000000000001m, 28, 28 },
            new object[] { -1, -0.0000000000000000000000000001m, 28, 28 },
            new object[] { 1, 0.00000000000000000000000000001m, 29, 29 }, // decimal value overflow (decimal has only 28 signinifant digits), in the result zero value is used
            new object[] { BigInteger.Parse("9999999999999999999999999999999999999999"), 10000000000000000000000000000m, 12, 28 },
        };

        [Theory]
        [MemberData(nameof(Data))]
        public void CanConvertToDecimal(
            BigInteger bigIntegerValue,
            decimal decimalValue,
            int decimals,
            int targetDecimals)
        {
            var value = bigIntegerValue.ToDecimal(decimals, targetDecimals);

            Assert.Equal(decimalValue, value);
        }

        public static IEnumerable<object[]> OverflowData = new List<object[]>
        {
            new object[] { BigInteger.Parse("9999999999999999999999999999999999999999"), 0, 0 },
            new object[] { BigInteger.Parse("-9999999999999999999999999999999999999999"), 0, 0 },
        };

        [Theory]
        [MemberData(nameof(OverflowData))]
        public void CanCatchConvertToDecimalOverflow(
            BigInteger bigIntegerValue,
            int decimals,
            int targetDecimals)
        {
            Assert.Throws<OverflowException>(() =>
            {
                var value = bigIntegerValue.ToDecimal(decimals, targetDecimals);
            });
        }
    }
}