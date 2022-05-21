using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Netezos.Encoding;
using Netezos.Forging.Models;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallets.Tezos
{
    public class TezosAccount : Account
    {
        private static TezosOperationsBatcher _operationsBatcher;
        public static TezosOperationsBatcher OperationsBatcher
        {
            get
            {
                var instance = _operationsBatcher;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _operationsBatcher, new TezosOperationsBatcher(), null);
                    instance = _operationsBatcher;
                }

                return instance;
            }
        }

        public TezosConfig Configuration => CurrencyConfigProvider
            .GetByName<TezosConfig>(Currency);

        public TezosAccount(
            string currency,
            IWalletProvider walletFactory,
            ICurrencyConfigProvider currencyConfigProvider,
            IWalletDataRepository dataRepository,
            ILogger logger = null)
            : base(
                  currency,
                  walletFactory,
                  currencyConfigProvider,
                  dataRepository,
                  logger)
        {
        }

        #region Sending

        public Task<(TezosOperation tx, Error error)> SendTransactionAsync(
            string from,
            string to,
            long amount,
            Fee fee,
            GasLimit gasLimit,
            StorageLimit storageLimit,
            Counter counter,
            string entrypoint = null,
            string parameters = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                return await OperationsBatcher
                    .SendOperationAsync(
                        account: this,
                        content: new TransactionContent
                        {
                            Amount = amount,
                            Destination = to,
                            Parameters = parameters != null
                                ? new Parameters
                                {
                                    Entrypoint = entrypoint,
                                    Value = Micheline.FromJson(parameters)
                                }
                                : null
                        },
                        from: from,
                        fee: fee,
                        gasLimit: gasLimit,
                        storageLimit: storageLimit,
                        counter: counter,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            }, cancellationToken);
        }

        #endregion Sending

        #region Signing

        public Task<Error> SignAsync(
            TezosOperation operation,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var walletAddress = await GetAddressAsync(operation.From, cancellationToken)
                        .ConfigureAwait(false);

                    var walletInfo = await DataRepository
                        .GetWalletInfoByIdAsync(walletAddress.WalletId, cancellationToken)
                        .ConfigureAwait(false);

                    if (walletInfo == null)
                        return new Error(
                            code: Errors.SigningError,
                            description: $"Can't find wallet info with id {walletAddress.WalletId} in account data repository");

                    using var wallet = WalletProvider.GetWallet(walletInfo);

                    if (wallet == null)
                        return new Error(
                            code: Errors.SigningError,
                            description: $"Can't create wallet with id {walletAddress.WalletId}");

                    var forgedOperationWithPrefix = await operation
                        .ForgeAsync(addOperationPrefix: true)
                        .ConfigureAwait(false);

                    operation.Signature = await wallet
                        .SignAsync(
                            data: forgedOperationWithPrefix,
                            keyPath: walletAddress.KeyPath,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    return null; // no errors
                }
                catch (Exception e)
                {
                    var error = $"Signing error: {e.Message}";

                    Logger.LogError(error);

                    return new Error(Errors.SigningError, error);
                }

            }, cancellationToken);
        }

        #endregion Signing

        #region Balances

        public override IWalletScanner GetWalletScanner() =>
            new TezosWalletScanner(this, WalletProvider, Logger);

        #endregion Balances

        #region Common

        internal async Task<SecureBytes> GetPublicKeyAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await GetAddressAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Logger.LogError($"Can't find wallet with address {address} in account data repository");

                return null;
            }

            var walletInfo = await DataRepository
                .GetWalletInfoByIdAsync(walletAddress.WalletId, cancellationToken)
                .ConfigureAwait(false);

            if (walletInfo == null)
            {
                Logger.LogError($"Can't find wallet info with id {walletAddress.WalletId} in account data repository");

                return null;
            }

            using var wallet = WalletProvider.GetWallet(walletInfo);

            if (wallet == null)
            {
                Logger.LogError($"Can't create wallet with id {walletAddress.WalletId}");

                return null;
            }

            return await wallet
                .GetPublicKeyAsync(walletAddress.KeyPath, cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion Common
    }
}