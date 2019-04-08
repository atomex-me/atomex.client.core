using System;

namespace Atomix.Updates
{
    public class NoUpdatesException : Exception
    {
        public NoUpdatesException()
            : base("Application is up to date")
        {

        }
    }
}
