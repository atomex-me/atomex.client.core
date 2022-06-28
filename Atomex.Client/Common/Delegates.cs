using Atomex.Client.V1.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Atomex.Client.Common
{
    public delegate string SwapContractResolver(string currency);
    public delegate Task<(byte[] publicKey, byte[] signature)> AuthMessageSigner(byte[] message, string algorithm);
    public delegate IEnumerable<Swap> LocalSwapProvider();
}