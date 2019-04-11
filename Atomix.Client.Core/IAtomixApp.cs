using Atomix.MarketData.Abstract;
using Atomix.Subsystems;
using Atomix.Subsystems.Abstract;
using Atomix.Wallet.Abstract;
using Microsoft.Extensions.Configuration;
using System;

namespace Atomix
{
    public interface IAtomixApp
    {
        event EventHandler<AccountChangedEventArgs> AccountChanged;

        IConfiguration Configuration { get; }
        IAccount Account { get; }
        ICurrencyQuotesProvider QuotesProvider { get; }
        ITerminal Terminal { get; }
        bool HasAccount { get; }
        bool HasQuotesProvider { get; }
        bool HasTerminal { get; }

        IAtomixApp UseAccount(IAccount account, bool restartTerminal = false);
    }
}