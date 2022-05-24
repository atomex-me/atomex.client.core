using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallets.Tezos.Fa12
{
    public class Fa12Account : Account
    {
        public Fa12Config Configuration => CurrencyConfigProvider
            .GetByName<Fa12Config>(Currency);

        public Fa12Account(
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

        public Task<(TezosOperation tx, Error error)> TransferAsync(
            string from,
            string to,
            decimal amount,
            Fee fee,
            GasLimit gasLimit,
            StorageLimit storageLimit,
            CancellationToken cancellationToken = default)
        {
            var tezosAccount = GetTezosAccount();
            var currencyConfig = Configuration;

            return tezosAccount
                .SendTransactionAsync(
                    from: from,
                    to: currencyConfig.TokenContract,
                    amount: 0,
                    fee: fee,
                    gasLimit: gasLimit,
                    storageLimit: storageLimit,
                    entrypoint: "transfer",
                    parameters: Fa12Helper.TransferParameters(from, to, amount),
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