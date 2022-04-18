using Microsoft.AspNetCore.SignalR.Client;

namespace Atomex.TzktEvents.Services
{
    public class HubConnectionCreator : IHubConnectionCreator
    {
        public HubConnection Create(string url)
        {
            var hubConnection = new HubConnectionBuilder()
                .WithUrl(url)
                .Build();

            return hubConnection;
        }
    }
}
