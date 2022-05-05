using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Netezos.Encoding;
using Netezos.Forging;
using Netezos.Forging.Models;
using Newtonsoft.Json.Linq;

using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Abstract;
using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Wallets.Abstract;
using Atomex.Wallets.Common;
using Atomex.Wallets.Tezos.Common;

namespace Atomex.Wallets.Tezos
{
    public class TezosAccount : Account
    {
        private static ResourceLocker<string> _addressLocker;
        public static ResourceLocker<string> AddressLocker
        {
            get
            {
                var instance = _addressLocker;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _addressLocker, new ResourceLocker<string>(), null);
                    instance = _addressLocker;
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

        public Task<(TezosOperation tx, Error error)> SendAsync(
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
                try
                {
                    if (counter.UseSync)
                        await AddressLocker
                            .LockAsync(from, cancellationToken)
                            .ConfigureAwait(false);

                    var (operation, error) = await FillOperationAsync(
                            operationType: TezosOperationType.Transaction,
                            from: from,
                            to: to,
                            amount: amount,
                            fee: fee,
                            gasLimit: gasLimit,
                            storageLimit: storageLimit,
                            counter: counter,
                            entrypoint: entrypoint,
                            parameters: parameters,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (error != null)
                        return (tx: null, error);

                    return await SendOperationAsync(
                            operation,
                            sign: true,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    if (counter.UseSync)
                        AddressLocker.Unlock(from);
                }

            }, cancellationToken);
        }

        public Task<(TezosOperation tx, Error error)> SendOperationAsync(
            TezosOperation operation,
            bool sign = true,
            CancellationToken cancellationToken = default)
        {
            return Task.Run<(TezosOperation tx, Error error)>(async () =>
            {
                try
                {
                    var walletAddress = await GetAddressAsync(operation.From, cancellationToken)
                        .ConfigureAwait(false);

                    // operation signing
                    if (sign)
                    {
                        var signError = await SignAsync(operation, cancellationToken)
                            .ConfigureAwait(false);

                        if (signError != null)
                            return (tx: null, error: signError);
                    }
                    else if (operation.Signature == null)
                    {
                        return (
                            tx: null,
                            error: new Error(
                                code: Errors.SendingError,
                                description: "Transaction is not signed"));
                    }

                    var currencyConfig = Configuration;

                    var api = new TezosApi(
                        settings: currencyConfig.ApiSettings,
                        logger: Logger);

                    // operation broadcast
                    var (txId, broadcastError) = await api
                        .BroadcastAsync(operation, cancellationToken)
                        .ConfigureAwait(false);

                    if (broadcastError != null)
                        return (tx: null, error: broadcastError);

                    var upsertResult = DataRepository
                        .UpsertTransactionAsync(operation, cancellationToken)
                        .ConfigureAwait(false);

                    return (tx: operation, error: null);
                }
                catch (Exception e)
                {
                    var error = $"Sending error: {e.Message}";

                    Logger.LogError(error);

                    return (tx: null, error: new Error(Errors.SendingError, error));
                }

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

        private Task<(TezosOperation operation, Error error)> FillOperationAsync(
            TezosOperationType operationType,
            string from,
            string to,
            long amount,
            Fee fee,
            GasLimit gasLimit,
            StorageLimit storageLimit,
            Counter counter,
            string entrypoint = null,
            string parameters = null,
            int headOffset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.Run<(TezosOperation operation, Error error)>(async () =>
            {
                using var securePublicKey = await GetPublicKeyAsync(from, cancellationToken)
                    .ConfigureAwait(false);

                using var publicKey = securePublicKey.ToUnmanagedBytes();

                var tezosConfig = Configuration;

                var api = new TezosApi(
                    settings: tezosConfig.ApiSettings,
                    logger: Logger);

                var (currentHeaderHash, currentHeaderError) = await api
                    .GetHeaderAsync(offset: 0, cancellationToken)
                    .ConfigureAwait(false);

                if (currentHeaderError != null)
                    return (operation: null, error: currentHeaderError);

                var (isRevealed, isRevealedError) = await api
                    .IsRevealedAsync(from, cancellationToken)
                    .ConfigureAwait(false);

                if (isRevealedError != null)
                    return (operation: null, error: isRevealedError);

                var numberOfCounters = isRevealed ? 1 : 2;

                var (counterValue, counterError) = await counter
                    .GetCounterAsync(
                        from: from,
                        numberOfCounters: numberOfCounters,
                        api: api,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (counterError != null)
                    return (operation: null, error: counterError);

                var operations = new List<ManagerOperationContent>();

                if (!isRevealed)
                {
                    operations.Add(new RevealContent
                    {
                        Source       = from,
                        PublicKey    = Base58Check.Encode(publicKey, TezosPrefixes.Edpk),
                        StorageLimit = 0,
                        GasLimit     = tezosConfig.RevealGasLimit,
                        Fee          = 0,
                        Counter      = counterValue.Value
                    });

                    counterValue++;
                }

                if (operationType == TezosOperationType.Transaction)
                {
                    operations.Add(new TransactionContent
                    {
                        Source       = from,
                        Destination  = to,
                        Amount       = amount,
                        StorageLimit = storageLimit.Value,
                        GasLimit     = gasLimit.Value,
                        Fee          = fee.Value,
                        Counter      = counterValue.Value,
                        Parameters   = parameters != null
                            ? new Parameters {
                                Entrypoint = entrypoint,
                                Value = Micheline.FromJson(parameters)
                            }
                            : null
                    });
                }
                else if (operationType == TezosOperationType.Delegation)
                {
                    operations.Add(new DelegationContent
                    {
                        Source       = from,
                        Delegate     = to,
                        StorageLimit = storageLimit.Value,
                        GasLimit     = gasLimit.Value,
                        Fee          = fee.Value,
                        Counter      = counterValue.Value
                    });
                }
                else
                {
                    // todo: return error: not supported tezos operation type
                }

                var (headerHash, headerError) = headOffset != 0
                    ? await api
                        .GetHeaderAsync(offset: headOffset, cancellationToken)
                        .ConfigureAwait(false)
                    : (currentHeaderHash, null);

                if (headerError != null)
                    return (operation: null, error: headerError);

                var useNetwork = fee.UseNetwork || gasLimit.UseNetwork || storageLimit.UseNetwork;

                if (useNetwork)
                {
                    var autoFillError = await AutoFillAsync(
                            operations: operations,
                            blockHash: headerHash,
                            chainId: Configuration.ChainId,
                            useNetworkFee: fee.UseNetwork,
                            useNetworkGasLimit: gasLimit.UseNetwork,
                            useNetworkStorageLimit: storageLimit.UseNetwork,
                            useSafeStorageLimit: storageLimit.UseSafeValue,
                            api: api,
                            tezosConfig: tezosConfig,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    // todo: handle auto fill error
                }

                return (
                    operation: new TezosOperation(operations, headerHash),
                    error: null
                );

            }, cancellationToken);
        }

        private static async Task<Error> AutoFillAsync(
            IEnumerable<ManagerOperationContent> operations,
            string blockHash,
            string chainId,
            bool useNetworkFee,
            bool useNetworkGasLimit,
            bool useNetworkStorageLimit,
            bool useSafeStorageLimit,
            ITezosApi api,
            TezosConfig tezosConfig,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (jsonResponse, error) = await api
                    .RunOperationsAsync(
                        branch: blockHash,
                        chainId: chainId,
                        operations: JsonSerializer.Serialize(operations),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var response = JObject.Parse(jsonResponse); // todo: use System.Text.Json instead

                foreach (var result in response["contents"])
                {
                    var metaData = result["metadata"];
                    var operationResult = metaData?["operation_result"];

                    if (operationResult?["status"]?.ToString() != "applied")
                        return new Error(Errors.AutoFillError, "At least one of the operations is not applied");

                    var operation = operations
                        .FirstOrDefault(c => c.Counter.ToString() == result["counter"].ToString());

                    // gas limit
                    if (useNetworkGasLimit)
                    {
                        var gas = tezosConfig.ReserveGasLimit
                            + operationResult?["consumed_gas"]?.Value<int>() ?? 0;

                        gas += metaData
                            ?["internal_operation_results"]
                            ?.Sum(res => res["result"]?["consumed_gas"]?.Value<int>() ?? 0) ?? 0;

                        operation.GasLimit = gas;
                    }

                    // storage limit
                    if (useNetworkStorageLimit)
                    {
                        var isAllocatedContract = operationResult
                            ?["allocated_destination_contract"]
                            ?.ToString() == "True" ? 1 : 0;

                        var storageDiff = operationResult
                            ?["paid_storage_size_diff"]
                            ?.Value<int>() ?? 0;

                        storageDiff += tezosConfig.ActivationStorageLimit * isAllocatedContract;

                        storageDiff += tezosConfig.ActivationStorageLimit * metaData
                            ?["internal_operation_results"]
                            ?.Where(res => res["result"]?["allocated_destination_contract"]?.ToString() == "True")
                            .Count() ?? 0;

                        storageDiff += metaData
                            ?["internal_operation_results"]
                            ?.Sum(res => res["result"]?["paid_storage_size_diff"]?.Value<int>() ?? 0) ?? 0;

                        operation.StorageLimit = useSafeStorageLimit
                            ? Math.Max(operation.StorageLimit, storageDiff)
                            : storageDiff;
                    }

                    // fee
                    if (useNetworkFee)
                    {
                        var forged = await new LocalForge()
                            .ForgeOperationAsync(blockHash, operation)
                            .ConfigureAwait(false);

                        var size = forged.Length
                            + Math.Ceiling((TezosConfig.HeadSizeInBytes + TezosConfig.SignatureSizeInBytes) / (decimal)operations.Count());

                        operation.Fee = (long)(tezosConfig.MinimalFee
                            + tezosConfig.MinimalNanotezPerByte * size
                            + Math.Ceiling(tezosConfig.MinimalNanotezPerGasUnit * operation.GasLimit))
                            + 10;
                    }
                }

                return null;
            }
            catch (Exception e)
            {
                return new Error(Errors.AutoFillError, e.Message);
            }
        }

        private async Task<SecureBytes> GetPublicKeyAsync(
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

        public static Task<ResourceLock<string>> LockAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return AddressLocker.GetLockAsync(address, cancellationToken);
        }

        #endregion Common
    }
}