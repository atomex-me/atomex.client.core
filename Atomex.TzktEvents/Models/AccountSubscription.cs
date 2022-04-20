using System;

namespace Atomex.TzktEvents.Models
{
    public class AccountSubscription
    {
        public string Address { get; }
        public Action Handler { get; }
        public int LastState { get; set; }

        public AccountSubscription(string address, Action handler, int lastState = 0)
        {
            Address = address;
            Handler = handler;
            LastState = lastState;
        }
    }
}
