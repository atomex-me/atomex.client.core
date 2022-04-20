using System;
using System.Threading.Tasks;

namespace Atomex.TzktEvents.Services
{
    public interface IService : IDisposable
    {
        Task InitAsync();
        void SetSubscriptions();
    }
}
