using System;

namespace Atomix.Core
{
    public class ErrorEventArgs : EventArgs
    {
        public Error Error { get; }

        public ErrorEventArgs(Error error)
        {
            Error = error;
        }
    }
}