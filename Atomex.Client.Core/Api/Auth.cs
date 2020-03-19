using System;
using System.Text;

namespace Atomex.Api
{
    public class Auth
    {
        public DateTime TimeStamp { get; set; }
        public string Nonce { get; set; }
        public string ClientNonce { get; set; }
        public string PublicKeyHex { get; set; }
        public string Signature { get; set; }
        public string Version { get; set; }

        public byte[] SignedData => Encoding.UTF8.GetBytes($"{TimeStamp:u}{Nonce}{ClientNonce}");
    }
}