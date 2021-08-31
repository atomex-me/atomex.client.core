namespace Atomex.Blockchain.Tezos
{
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
}