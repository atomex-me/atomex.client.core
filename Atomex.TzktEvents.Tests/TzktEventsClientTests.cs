using System;
using System.Threading.Tasks;
using Atomex.TzktEvents.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Moq;
using Xunit;


namespace Atomex.TzktEvents.Tests
{
    public class TzktEventsClientTests
    {
        [Fact]
        public void Constructor_NullInput_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() => new TzktEventsClient(null, null));
        }

        // Seems like there is an issue with testing SignalR clients cause of HubConnection implementation:
        // See also: https://github.com/dotnet/aspnetcore/issues/14924
        /*[Fact]
        public async Task Start_WithUri_ShouldSetUpEventsUrl()
        {
            var hubConnectionCreator = new Mock<IHubConnectionCreator>();
            var hubConnection = new Mock<HubConnection>();
            hubConnectionCreator.Setup(x => x.Create(It.IsAny<string>()))
                .Returns(hubConnection.Object);
            var client = new TzktEventsClient(hubConnectionCreator.Object);
            const string baseUri = "api.tzkt.io";

            await client.Start(baseUri);

            Assert.Equal("https://api.tzkt.io/v1/events", client.EventsUrl);
        }*/
    }
}
