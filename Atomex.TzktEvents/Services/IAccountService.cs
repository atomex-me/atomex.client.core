using System;
using System.Threading.Tasks;


namespace Atomex.TzktEvents.Services
{
    public interface IAccountService : IService
    {
        Task NotifyOnAccountAsync(string address, Action<string> handler);
    }
}
