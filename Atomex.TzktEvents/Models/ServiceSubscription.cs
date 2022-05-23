using System;

namespace Atomex.TzktEvents.Models
{
    public record ServiceSubscription(Action<string> Handler, int LastState = 0);
}
