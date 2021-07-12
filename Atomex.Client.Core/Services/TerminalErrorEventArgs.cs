using Atomex.Core;

namespace Atomex.Services
{
    public class TerminalErrorEventArgs : TerminalServiceEventArgs
    {
        public Error Error { get; }
        public int Code => Error.Code;
        public string Description => Error.Description;

        public TerminalErrorEventArgs(TerminalService service, Error error)
            : base(service)
        {
            Error = error;
        }
    }
}