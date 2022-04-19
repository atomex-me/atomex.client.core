﻿using System;
using System.Threading.Tasks; 

namespace Atomex.TzktEvents
{
    public interface ITzktEventsClient
    {
        string BaseUri { get; }

        string EventsUrl { get; }

        Task StartAsync(string baseUri);
        Task StopAsync();

        Task NotifyOnAccount(string address, Action handler);
    }
}