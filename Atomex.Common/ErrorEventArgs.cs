using System;

namespace Atomex.Common
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