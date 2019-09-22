using System.Threading.Tasks;

namespace Atomex.Subsystems.Abstract
{
    public interface ICurrenciesUpdater
    {
        Task UpdateAsync();
    }
}