using System;
using Atomix.Common;
using Atomix.MarketData.Abstract;
using Atomix.Subsystems;
using Atomix.Subsystems.Abstract;
using Atomix.Wallet.Abstract;
using Microsoft.Extensions.Configuration;

namespace Atomix
{
    public class AtomixApp : IAtomixApp
    {
        public event EventHandler<AccountChangedEventArgs> AccountChanged;

        public IConfiguration Configuration { get; }
        public IAccount Account { get; private set; }
        public ICurrencyQuotesProvider QuotesProvider { get; private set; }
        public ITerminal Terminal { get; private set; }
        public bool HasAccount => Account != null;
        public bool HasQuotesProvider => QuotesProvider != null;
        public bool HasTerminal => Terminal != null;

        public AtomixApp(IConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public IAtomixApp UseAccount(IAccount account, bool restartTerminal = false)
        {
            var previousAccount = Account;
            Account = account;

            AccountChanged?.Invoke(this, new AccountChangedEventArgs(previousAccount, Account));

            if (HasTerminal)
                Terminal.ChangeAccountAsync(account, restartTerminal).FireAndForget();

            return this;
        }

        public AtomixApp UseQuotesProvider(ICurrencyQuotesProvider quotesProvider)
        {
            QuotesProvider = quotesProvider;
            return this;
        }

        public AtomixApp UseTerminal(ITerminal terminal)
        {
            Terminal = terminal;
            return this;
        }

        public AtomixApp Start()
        {
            if (HasQuotesProvider)
                QuotesProvider.Start();

            if (HasTerminal && HasAccount) // now client can connect only with authorization by wallet
                Terminal.StartAsync().FireAndForget();

            return this;
        }

        public AtomixApp Stop()
        {
            if (HasQuotesProvider)
                QuotesProvider.Stop();

            if (HasTerminal)
                Terminal.StopAsync().FireAndForget();

            return this;
        }
    }
}