using System;
using Atomex.Wallet.Abstract;

namespace Atomex.Subsystems
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