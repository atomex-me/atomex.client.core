using System.Threading.Tasks;

namespace Atomex.TzktEvents
{
    public interface ITzktEventsClient
    {
        string BaseUri { get; }

        Task Start();
        Task Stop();
    }
}
