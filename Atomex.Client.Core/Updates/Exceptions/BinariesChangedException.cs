using System;

namespace Atomex.Updates
{
    public class BinariesChangedException : Exception
    {
        public BinariesChangedException()
            : base("Loaded binaries has been changed")
        {

        }
    }
}
