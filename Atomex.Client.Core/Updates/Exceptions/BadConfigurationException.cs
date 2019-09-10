using System;

namespace Atomex.Updates
{
    public class BadConfigurationException : Exception
    {
        public BadConfigurationException()
            : base("Updater doesn't configured properly")
        {

        }
    }
}
