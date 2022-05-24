using System;
using System.Collections.Generic;
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
        private static TezosOperationPerBlockManager _operationsBatcher;
        public static TezosOperationPerBlockManager OperationsBatcher
        {
            get
            {
                var instance = _operationsBatcher;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _operationsBatcher, new TezosOperationPerBlockManager(), null);
                    instance = _operationsBatcher;
                }

                return instance;
            }
        }

        public TezosConfig Configuration => CurrencyConfigProvider
            .GetByName<TezosConfig>(Currency);

        public TezosAccount(
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

        public Task<(TezosOperation tx, Error error)> SendTransactionAsync(
            string from,
            string to,
            long amount,
            Fee fee,
            GasLimit gasLimit,
            StorageLimit storageLimit,
            string entrypoint = null,
            string parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (fee == null)
                throw new ArgumentNullException(nameof(fee), "Fee must be not null. Please use Fee.FromValue() or Fee.FromNetwork()");

            if (gasLimit == null)
                throw new ArgumentNullException(nameof(gasLimit), "GasLimit must be not null. Please use GasLimit.FromValue() or GasLimit.FromNetwork()");

            if (storageLimit == null)
                throw new ArgumentNullException(nameof(storageLimit), "StorageLimit must be not null. Please use StorageLimit.FromValue() or StorageLimit.FromNetwork()");

            return Task.Run(async () =>
            {
                return await OperationsBatcher
                    .SendOperationsAsync(
                        account: this,
                        operationsParameters: new List<TezosOperationParameters> {
                            new TezosOperationParameters {
                                Content = new TransactionContent
                                {
                                    Source       = from,
                                    Amount       = amount,
                                    Destination  = to,
                                    Fee          = fee.Value,
                                    GasLimit     = gasLimit.Value,
                                    StorageLimit = storageLimit.Value,
                                    Parameters   = parameters != null
                                        ? new Parameters
                                        {
                                            Entrypoint = entrypoint,
                                            Value = Micheline.FromJson(parameters)
                                        }
                                        : null
                                },
                                From         = from,
                                Fee          = fee,
                                GasLimit     = gasLimit,
                                StorageLimit = storageLimit
                            }
                        },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            }, cancellationToken);
        }

        public async Task<(TezosOperation tx, Error error)> SendUnmanagedOperationAsync(
            TezosOperation operation,
            CancellationToken cancellationToken = default)
        {
            if (operation.IsManaged())
                throw new InvalidOperationException("Method must be used for sending unmanaged operations only");

            return await SendOperationAsync(operation, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Send managed operation without single per block operation control. If the replaced operation has already been confirmed, the method will return an error.
        /// </summary>
        /// <param name="operation">Replacement operation</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Operation if success, otherwise error</returns>
        public async Task<(TezosOperation tx, Error error)> ReplaceOperationAsync(
            TezosOperation operation,
            CancellationToken cancellationToken = default)
        {
            if (!operation.IsManaged())
                throw new InvalidOperationException("Method must be used for sending managed operations only");

            return await SendOperationAsync(operation, cancellationToken)
                .ConfigureAwait(false);
        }

        internal Task<(TezosOperation tx, Error error)> SendOperationAsync(
            TezosOperation operation,
            CancellationToken cancellationToken = default)
        {
            return Task.Run<(TezosOperation tx, Error error)>(async () =>
            {
                // sign the operation
                var error = await SignAsync(operation, cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return (tx: null, error);

                // broadcast the operation
                var currencyConfig = Configuration;

                var api = new TezosApi(
                    settings: currencyConfig.ApiSettings,
                    logger: Logger);

                var (txId, broadcastError) = await api
                    .BroadcastAsync(operation, cancellationToken)
                    .ConfigureAwait(false);

                if (broadcastError != null)
                    return (tx: null, error: broadcastError);

                // save operation in local db
                var upsertResult = DataRepository
                    .UpsertTransactionAsync(operation, cancellationToken)
                    .ConfigureAwait(false);

                return (tx: operation, error: null);

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