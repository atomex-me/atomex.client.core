using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Common;
using Atomex.Blockchain.Ethereum;
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

        public Task<(EthereumTransaction tx, Error error)> SendTransferAsync(
            string from,
            string to,
            BigInteger amountInTokens,
            GasPrice gasPrice,
            GasLimit gasLimit,
            Nonce nonce,
            CancellationToken cancellationToken = default)
        {
            var ethereumAccount = GetEthereumAccount();
            var currencyConfig = Configuration;

            return ethereumAccount
                .SendErc20TransferAsync(
                    from: from,
                    to: to,
                    tokenContract: currencyConfig.TokenContract,
                    amountInTokens: amountInTokens,
                    gasPrice: gasPrice,
                    gasLimit: gasLimit,
                    nonce: nonce,
                    cancellationToken: cancellationToken);
        }

        public Task<(EthereumTransaction tx, Error error)> SendTransferAsync(
            string from,
            string to,
            decimal amount,
            GasPrice gasPrice,
            GasLimit gasLimit,
            Nonce nonce,
            CancellationToken cancellationToken = default)
        {
            return SendTransferAsync(
                from: from,
                to: to,
                amountInTokens: TokensHelper.TokensToTokenUnits(
                    tokens: amount,
                    decimals: Configuration.Decimals),
                gasPrice: gasPrice,
                gasLimit: gasLimit,
                nonce: nonce,
                cancellationToken: cancellationToken);
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