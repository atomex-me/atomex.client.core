using System.Threading.Tasks;


namespace Atomex.TzktEvents.Services
{
    public interface IService
    {
        Task InitAsync();
        void SetSubscriptions();
    }
}
