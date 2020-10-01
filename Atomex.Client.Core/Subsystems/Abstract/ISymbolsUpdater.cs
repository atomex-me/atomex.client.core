using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Subsystems.Abstract
{
    public interface ISymbolsUpdater
    {
        void Start();
        void Stop();
        Task UpdateAsync(CancellationToken cancellationToken);
    }
}