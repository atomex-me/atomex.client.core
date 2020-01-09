using System.Collections.Generic;

namespace Atomex.Core
{
    public class AddressUsageEstimation
    {
        public decimal Amount { get; set; }
        public decimal Fee { get; set; }
        public string Address { get; set; }
    }

    public class FundsUsageEstimation
    {
        public decimal TotalAmount { get; set; }
        public decimal TotalFee { get; set; }
        public decimal FeePrice { get; set; }
        public IEnumerable<AddressUsageEstimation> UsedAddresses { get; set; }
    }
}