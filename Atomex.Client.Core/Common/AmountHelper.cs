using Atomex.Core;

namespace Atomex.Common
{
    public static class AmountHelper
    {
        public static decimal AmountToQty(Side side, decimal amount, decimal price) =>
            side == Side.Buy ? amount / price : amount;

        public static decimal QtyToAmount(Side side, decimal qty, decimal price) =>
            side == Side.Buy ? qty * price : qty;
    }
}