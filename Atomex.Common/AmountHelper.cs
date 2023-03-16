using System;
using System.Numerics;

namespace Atomex.Common
{
    public static class AmountHelper
    {
        public static decimal AmountToSellQty(
            Side side,
            decimal amount,
            decimal price,
            int precision)
        {
            return RoundDown(side == Side.Buy ? amount / price : amount, precision);
        }

        public static decimal AmountToBuyQty(
            Side side,
            decimal amount,
            decimal price,
            int precision) => AmountToSellQty(side.Opposite(), amount, price, precision);

        public static decimal QtyToSellAmount(
            Side side,
            decimal qty,
            decimal price,
            int precision)
        {
            return RoundDown(side == Side.Buy ? qty * price : qty, precision);
        }

        public static decimal QtyToBuyAmount(
            Side side,
            decimal qty,
            decimal price,
            int precision) => QtyToSellAmount(side.Opposite(), qty, price, precision);

        public static decimal RoundDown(decimal d, int precision)
        {
            const int MaxDecimalPrecision = 28;

            try
            {
                if (precision < MaxDecimalPrecision)
                {
                    var multiplier = (decimal)Math.Pow(10, precision);

                    var integral = Math.Truncate(d);
                    var fraction = d - integral;

                    return integral + Math.Truncate(fraction * multiplier) / multiplier;
                }
            }
            catch
            {
                // nothing todo
            }

            return d
                .Multiply(BigInteger.Pow(10, precision))
                .ToDecimal(
                    valueDecimals: precision,
                    resultDecimals: Math.Min(precision, MaxDecimalPrecision));
        }

        public static BigInteger DustProofMin(
            BigInteger amount,
            BigInteger refAmount,
            long dustMultiplier)
        {
            var rest = amount - refAmount;

            return rest < dustMultiplier
                ? amount
                : refAmount;
        }
    }
}