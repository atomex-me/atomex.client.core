namespace Atomix.Core
{
    public enum OrderType
    {
        Return = 0x00,
        /// <summary>
        /// Order is either fully executed or completely canceled
        /// </summary>
        FillOrKill = 0x01,
        /// <summary>
        /// Order
        /// </summary>
        ImmediateOrCancel = 0x02,
        /// <summary>
        /// Order with user-fixed commission is either fully executed or completely canceled
        /// </summary>
        DirectFillOrKill = 0x03,
    }
}