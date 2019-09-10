using System;

namespace Atomex.Core
{
    public class InternalException : Exception
    {
        public Error Error { get; }

        public InternalException(Error error)
            : base(error.Description)
        {
            Error = error;
        }

        public InternalException(int code, string description)
            : this(new Error(code, description))
        {
        }
    }
}