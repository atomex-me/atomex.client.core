using System;

namespace Atomex.TzktEvents.Models
{
    public record ServiceSubscription(Action<string> Handler, long LastState = 0);
}
