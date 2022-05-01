using System;

namespace Atomex.Common
{
    public static class SideExtensions
    {
        public static Side Opposite(this Side side)
        {
            return side switch
            {
                Side.Buy  => Side.Sell,
                Side.Sell => Side.Buy,
                _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
            };
        }
    }
}