using System;

namespace Atomex.Blockchain.Tezos
{
    public enum DelegationStatus
    {
        Pending,
        Confirmed,
        Active
    }
    
    public class BakerData
    {
        public string Logo { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public decimal Fee { get; set; }
        public decimal MinDelegation { get; set; }
        public decimal StakingAvailable { get; set; }
        public decimal EstimatedRoi { get; set; }
    }

    public class Delegation
    {
        public BakerData Baker { get; set; }
        public string Address { get; set; }
        public string ExplorerUri { get; set; }
        public decimal Balance { get; set; }
        public DateTime DelegationTime { get; set; }
        public DelegationStatus Status { get; set; }
    }
}