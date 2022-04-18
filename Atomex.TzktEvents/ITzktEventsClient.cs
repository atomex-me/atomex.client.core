using System.Threading.Tasks;

namespace Atomex.TzktEvents
{
    public interface ITzktEventsClient
    {
        string BaseUri { get; }

        Task Start(string baseUri);
        Task Stop();
    }
}
