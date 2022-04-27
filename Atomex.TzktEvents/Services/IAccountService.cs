using System;
using System.Collections.Generic;
using System.Threading.Tasks;


namespace Atomex.TzktEvents.Services
{
    public interface IAccountService : IService
    {
        Task NotifyOnAccountAsync(string address, Action<string> handler);
        Task NotifyOnAccountsAsync(IEnumerable<string> addresses, Action<string> handler);
    }
}
