using System.Threading.Tasks;


namespace Atomex.Services.Abstract
{
    public interface IChainBalanceUpdater
    {
        Task StartAsync();
        Task StopAsync();
    }
}
