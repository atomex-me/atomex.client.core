using System.Collections.Generic;
using System.Threading.Tasks;

using Atomex.Client.V1.Entities;

namespace Atomex.Client.Common
{
    public delegate Task<(byte[] publicKey, byte[] signature)> AuthMessageSigner(byte[] message, string algorithm);
    public delegate IEnumerable<Swap> LocalSwapProvider();
}