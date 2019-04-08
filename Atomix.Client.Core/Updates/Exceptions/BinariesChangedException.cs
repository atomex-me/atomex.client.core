using System;

namespace Atomix.Updates
{
    public class BinariesChangedException : Exception
    {
        public BinariesChangedException()
            : base("Loaded binaries has been changed")
        {

        }
    }
}
