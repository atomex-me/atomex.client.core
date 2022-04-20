using System;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace Atomex.TzktEvents.Services
{
    public interface IAccountService : IDisposable
    {
        Task NotifyOnAccountAsync(string address, Action handler);
        Task InitAsync();
        void SetSubscriptions();
    }
}
