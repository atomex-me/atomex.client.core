using Atomex.Core;

namespace Atomex.Services
{
    public class AtomexClientErrorEventArgs : AtomexClientServiceEventArgs
    {
        public Error Error { get; }
        public int Code => Error.Code;
        public string Description => Error.Description;

        public AtomexClientErrorEventArgs(AtomexClientService service, Error error)
            : base(service)
        {
            Error = error;
        }
    }
}