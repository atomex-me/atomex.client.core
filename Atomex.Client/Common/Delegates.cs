using System.Threading.Tasks;

namespace Atomex.Client.Common
{
    public delegate Task<(byte[] publicKey, byte[] signature)> AuthMessageSigner(byte[] message, string algorithm);
    public delegate Task<long> LastLocalSwapIdProvider();
}