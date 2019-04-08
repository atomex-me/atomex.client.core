using System;

namespace Atomix.Updates
{
    public class ReadyEventArgs : EventArgs
    {
        public Version Version { get; }
        public string Installer { get; }

        public ReadyEventArgs(Version version, string installer)
        {
            Version = version;
            Installer = installer;
        }
    }
}
