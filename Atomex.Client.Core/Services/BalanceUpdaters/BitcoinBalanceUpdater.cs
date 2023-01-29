using Microsoft.Extensions.Logging;

using Atomex.Blockchain.SoChain;
using Atomex.Services.BalanceUpdaters.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex.Services.BalanceUpdaters
{
    public class BitcoinBalanceUpdater : BitcoinBasedBalanceUpdater
    {
        private const string CurrencyName = "BTC";

        public BitcoinBalanceUpdater(
            IAccount account,
            IWalletScanner walletScanner,
            ISoChainRealtimeApi api,
            ILogger log)
            : base(account, walletScanner, api, log, CurrencyName)
        {
        }
    }
}