using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Nethereum.Contracts;

using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Messages.Erc20;
using Atomex.Wallets.Abstract;
using Error = Atomex.Common.Error;

namespace Atomex.Wallets.Ethereum.Erc20
{
    public class Erc20Account : Account
    {
        public Erc20Config Configuration => CurrencyConfigProvider
            .GetByName<Erc20Config>(Currency);

        public Erc20Account(
            string currency,
            IWalletProvider walletProvider,
            ICurrencyConfigProvider currencyConfigProvider,
            IWalletDataRepository dataRepository,
            ILogger logger = null)
            : base(
                currency,
                walletProvider,
                currencyConfigProvider,
                dataRepository,
                logger)
        {
        }

        #region Sending

        public async Task<(EthereumTransaction tx, Error error)> TransferAsync(
            string from,
            string to,
            decimal amount,
            GasPrice gasPrice,
            GasLimit gasLimit,
            Nonce nonce,
            CancellationToken cancellationToken = default)
        {
            var ethereumAccount = GetEthereumAccount();
            var currencyConfig = Configuration;

            var message = new Erc20TransferMessage
            {
                To = to,
                Value = EthereumHelper.TokensToBaseTokenUnits(
                    amount,
                    currencyConfig.DecimalsMultiplier),
                FromAddress = from
            };

            var input = message
                .CreateTransactionInput(currencyConfig.TokenContract);

            return await ethereumAccount
                .SendAsync(
                    from: input.From,
                    to: input.To,
                    amount: EthereumHelper.WeiToEth(input.Value.Value),
                    gasPrice: gasPrice,
                    gasLimit: gasLimit,
                    nonce: nonce,
                    data: input.Data,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion Sending

        #region Balances

        public override IWalletScanner GetWalletScanner() =>
            new Erc20WalletScanner(this, WalletProvider, Logger);

        #endregion Balances

        #region Common

        public EthereumAccount GetEthereumAccount() => new(
            currency: EthereumHelper.Eth,
            walletProvider: WalletProvider,
            currencyConfigProvider: CurrencyConfigProvider,
            dataRepository: DataRepository,
            logger: Logger);

        #endregion Common
    }
}