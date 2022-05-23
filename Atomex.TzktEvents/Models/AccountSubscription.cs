using System;

namespace Atomex.TzktEvents.Models
{
    public record AccountSubscription(Action<string> Handler, int LastState = 0);
}
