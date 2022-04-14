using System;
using System.Threading.Tasks;

using Atomex.Blockchain.Tezos;


namespace Atomex.Services.Abstract
{
    public interface ITzktEventsClient
    {
        string BaseUrl { get; }

        Task Start();
        Task Stop();
    }
}
