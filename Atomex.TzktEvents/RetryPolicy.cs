using System;
using Microsoft.AspNetCore.SignalR.Client;

namespace Atomex.TzktEvents
{
    public class RetryPolicy : IRetryPolicy
    {
        private readonly int RECONNECT_TIMEOUT_SECONDS = 18;

        private TimeSpan? _retryDelay;

        public TimeSpan? NextRetryDelay(RetryContext retryContext) => _retryDelay ??= TimeSpan.FromSeconds(RECONNECT_TIMEOUT_SECONDS);
    }
}
