using System;

namespace Atomex.Wallet.Tezos
{
    public class TezosAddressInfo
    {
        public string Address { get; set; }
        public bool IsAllocated { get; set; }
        public bool IsRevealed { get; set; }
        public DateTime LastCheckTimeUtc { get; set; }
    }
}