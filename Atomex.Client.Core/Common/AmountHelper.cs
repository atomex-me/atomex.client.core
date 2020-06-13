using System;
using Atomex.Core;

namespace Atomex.Common
{
    public static class AmountHelper
    {
        public static decimal AmountToQty(
            Side side,
            decimal amount,
            decimal price,
            decimal digitsMultiplier)
        {
            return RoundDown(side == Side.Buy ? amount / price : amount, digitsMultiplier);
        }

        public static decimal QtyToAmount(
            Side side,
            decimal qty,
            decimal price,
            decimal digitsMultiplier)
        {
            return RoundDown(side == Side.Buy ? qty * price : qty, digitsMultiplier);
        }

        public static decimal RoundDown(decimal d, decimal digitsMultiplier) =>
            Math.Floor(d * digitsMultiplier) / digitsMultiplier;

        public static decimal DustProofMin(
            decimal amount,
            decimal refAmount,
            decimal digitsMultiplier,
            decimal dustMultiplier)
        {
            return RoundDown(amount - refAmount, digitsMultiplier / dustMultiplier) == 0 ? amount : Math.Min(amount, refAmount);
        }
    }
}