using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Common
{
    public class NonceEventArgs
    {
        public AuthNonce Nonce { get; private set; }

        public NonceEventArgs(AuthNonce nonce)
        {
            Nonce = nonce;
        }
    }
}