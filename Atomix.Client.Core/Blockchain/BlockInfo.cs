using System;

namespace Atomix.Blockchain
{
    public class BlockInfo : ICloneable
    {
        public long Fees { get; set; }
        public int Confirmations { get; set; }
        public long BlockHeight { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime BlockTime { get; set; }
        // public string BlockHash { get;set; }

        public object Clone()
        {
            return MemberwiseClone();
        }
    }
}