using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atomex.TzktEvents.Models;

namespace Atomex.TzktEvents.Services
{
    public interface ITokensService : IService
    {
        Task NotifyOnTokenBalancesAsync(string address, Action<TezosTokenEvent> handler);
        Task NotifyOnTokenBalancesAsync(IEnumerable<string> addresses, Action<TezosTokenEvent> handler);
    }
}
