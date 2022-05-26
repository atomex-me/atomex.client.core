using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallets.Tezos.Fa2
{
    public class Fa2Account : Account
    {
        public Fa2Config Configuration => CurrencyConfigProvider
            .GetByName<Fa2Config>(Currency);

        public Fa2Account(
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

        public Task<(TezosOperation tx, Error error)> SendTransferAsync(
            string from,
            string to,
            BigInteger amountInTokens,
            Fee fee,
            GasLimit gasLimit,
            StorageLimit storageLimit,
            CancellationToken cancellationToken = default)
        {
            var tezosAccount = GetTezosAccount();
            var currencyConfig = Configuration;

            return tezosAccount
                .SendFa2TransferAsync(
                    from: from,
                    to: to,
                    tokenContract: currencyConfig.TokenContract,
                    tokenId: currencyConfig.TokenId,
                    amountInTokens: amountInTokens,
                    fee: fee,
                    gasLimit: gasLimit,
                    storageLimit: storageLimit,
                    cancellationToken: cancellationToken);
        }

        public Task<(TezosOperation tx, Error error)> SendTransferAsync(
            string from,
            string to,
            decimal amount,
            Fee fee,
            GasLimit gasLimit,
            StorageLimit storageLimit,
            CancellationToken cancellationToken = default)
        {
            return SendTransferAsync(
                from: from,
                to: to,
                amountInTokens: TezosHelper.TokensToBaseTokenUnits(
                    amount,
                    Configuration.DecimalsMultiplier),
                fee: fee,
                gasLimit: gasLimit,
                storageLimit: storageLimit,
                cancellationToken: cancellationToken);
        }

        #endregion Sending

        #region Balances

        public override IWalletScanner GetWalletScanner()
        {
            throw new NotImplementedException();
        }

        #endregion Balances

        #region Common

        public TezosAccount GetTezosAccount() => new(
            currency: TezosHelper.Xtz,
            walletProvider: WalletProvider,
            currencyConfigProvider: CurrencyConfigProvider,
            dataRepository: DataRepository,
            logger: Logger);

        #endregion Common
    }
}