using System;
using Atomix.Wallet.Abstract;

namespace Atomix.Subsystems
{
    public class AccountChangedEventArgs : EventArgs
    {
        public IAccount OldAccount { get; }
        public IAccount NewAccount { get; }

        public AccountChangedEventArgs(IAccount oldAccount, IAccount newAccount)
        {
            OldAccount = oldAccount;
            NewAccount = newAccount;
        }
    }
}