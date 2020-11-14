using System;
namespace Atomex.Blockchain.Tezos
{
    public class Account
    {
        public string Address { get; set; }
        public string DelegateAddress { get; set; }
        public DateTime DelegationTime { get; set; }
        public decimal DelegationLevel { get; set; }
    }
}
