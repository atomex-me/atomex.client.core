using System;

namespace Atomex.Cryptography.Abstract
{
    public interface IIncrementalHash : IDisposable
    {
        void Initialize();
        void Update(ReadOnlySpan<byte> data);
        void Finalize(Span<byte> hash);
        byte[] Finalize();
    }
}