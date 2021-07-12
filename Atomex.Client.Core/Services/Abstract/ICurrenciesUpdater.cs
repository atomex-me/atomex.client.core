using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Services.Abstract
{
    public interface ICurrenciesUpdater
    {
        void Start();
        void Stop();
        Task UpdateAsync(CancellationToken cancellationToken);
    }
}