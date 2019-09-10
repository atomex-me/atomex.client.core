using System;

namespace Atomex.Updates
{
    public class NoUpdatesException : Exception
    {
        public NoUpdatesException()
            : base("Application is up to date")
        {

        }
    }
}
