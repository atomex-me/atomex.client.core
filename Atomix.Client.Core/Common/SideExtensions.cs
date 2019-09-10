using System;
using Atomix.Core;

namespace Atomix.Common
{
    public static class SideExtensions
    {
        public static Side Opposite(this Side side)
        {
            switch (side)
            {
                case Side.Buy:
                    return Side.Sell;
                case Side.Sell:
                    return Side.Buy;
                default:
                    throw new ArgumentOutOfRangeException(nameof(side), side, null);
            }
        }
    }
}