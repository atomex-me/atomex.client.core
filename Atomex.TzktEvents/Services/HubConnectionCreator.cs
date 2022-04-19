using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Atomex.TzktEvents.Services
{
    public class HubConnectionCreator : IHubConnectionCreator
    {
        public HubConnection Create(string url)
        {
            var hubConnection = new HubConnectionBuilder()
                .WithUrl(url)
                .AddNewtonsoftJsonProtocol()
                .Build();

            return hubConnection;
        }
    }
}
