using System;

namespace Atomex.Blockchain
{
    public class BlockInfo : ICloneable
    {
        public int Confirmations { get; set; }
        public string BlockHash { get; set; }
        public long BlockHeight { get; set; }
        public DateTime? BlockTime { get; set; }
        public DateTime? FirstSeen { get; set; }

        public object Clone()
        {
            return MemberwiseClone();
        }
    }
}