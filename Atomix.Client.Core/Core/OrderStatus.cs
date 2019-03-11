namespace Atomix.Core
{
    public enum OrderStatus
    {
        Unknown = 0x00,
        Pending = 0x01,
        Placed = 0x02,
        Canceled = 0x04,
        PartiallyFilled = 0x08,
        Filled = 0x10,
        Rejected = 0x20
    }
}