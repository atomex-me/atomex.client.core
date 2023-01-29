using Microsoft.Extensions.Logging;

using Atomex.Blockchain.SoChain;
using Atomex.Services.BalanceUpdaters.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex.Services.BalanceUpdaters
{
    public class LitecoinBalanceUpdater : BitcoinBasedBalanceUpdater
    {
        private const string CurrencyName = "LTC";

        public LitecoinBalanceUpdater(
            IAccount account,
            IWalletScanner walletScanner,
            ISoChainRealtimeApi api,
            ILogger log)
            : base(account, walletScanner, api, log, CurrencyName)
        {
        }
    }
}