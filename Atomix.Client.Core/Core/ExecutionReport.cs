using Atomix.Core.Entities;
using Atomix.Swaps;

namespace Atomix.Core
{
    public class ExecutionReport
    {
        public Order Order { get; set; }
        public SwapRequisites Requisites { get; set; }

        public ExecutionReport()
            : this(null, null)
        {
        }

        public ExecutionReport(Order order)
            : this(order, null)
        {
        }

        public ExecutionReport(Order order, SwapRequisites requisites)
        {
            Order = order;
            Requisites = requisites;
        }
    }
}