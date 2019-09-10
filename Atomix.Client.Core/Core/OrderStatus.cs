namespace Atomix.Core
{
    public enum OrderStatus
    {
        Pending,
        Placed,
        PartiallyFilled,
        Filled,
        Canceled,
        Rejected
    }
}