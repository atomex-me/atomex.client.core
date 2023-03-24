using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Wallets
{
    /// <summary>
    /// Represents basic interface for asymmetric Hierarchical Deterministic key
    /// </summary>
    public interface IExtKey : IKey
    {
        /// <summary>
        /// Derives child key with <paramref name="index"/>
        /// </summary>
        /// <param name="index">Index</param>
        /// <returns>Key</returns>
        IExtKey Derive(uint index);

        /// <summary>
        /// Derives child key with key derivation path <paramref name="keyPath"/>
        /// </summary>
        /// <param name="keyPath">Key derivation path</param>
        /// <returns>Key</returns>
        IExtKey Derive(string keyPath);
    }
}