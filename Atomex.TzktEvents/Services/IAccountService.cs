using System;
using System.Threading.Tasks;


namespace Atomex.TzktEvents.Services
{
    public interface IAccountService : IDisposable
    {
        Task InitAsync();
        void SetSubscriptions();
        Task NotifyOnAccountAsync(string address, Action handler);
    }
}
