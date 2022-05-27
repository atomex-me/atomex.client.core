using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Atomex.TzktEvents.Services
{
    public interface ITokensService : IService
    {
        Task NotifyOnTokenBalancesAsync(string address, Action<string> handler);
        Task NotifyOnTokenBalancesAsync(IEnumerable<string> addresses, Action<string> handler);
    }
}
