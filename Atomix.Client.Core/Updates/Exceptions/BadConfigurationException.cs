using System;

namespace Atomix.Updates
{
    public class BadConfigurationException : Exception
    {
        public BadConfigurationException()
            : base("Updater doesn't configured properly")
        {

        }
    }
}
