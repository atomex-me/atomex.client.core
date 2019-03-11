using Atomix.Core;

namespace Atomix.Common
{
    public static class AmountHelper
    {
        public static decimal AmountToQty(Side side, decimal amount, decimal price)
        {
            return side == Side.Buy ? amount / price : amount;
        }

        public static decimal QtyToAmount(Side side, decimal qty, decimal price)
        {
            return side == Side.Buy ? qty * price : qty;
        }
    }
}