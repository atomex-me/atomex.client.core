using Microsoft.AspNetCore.SignalR.Client;

namespace Atomex.TzktEvents.Services
{
    public interface IHubConnectionCreator
    {
        HubConnection Create(string url);
    }
}
