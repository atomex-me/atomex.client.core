using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Netezos.Forging;
using Netezos.Forging.Models;
using Newtonsoft.Json.Linq;

using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Abstract;
using Atomex.Common;
using Atomex.Cryptography;
using Atomex.Wallets.Tezos.Common;

namespace Atomex.Wallets.Tezos
{
    public static class TezosOperationFiller
    {
        public static Task<(TezosOperation operation, Error error)> FillOperationAsync(
             IEnumerable<TezosOperationParameters> operationsRequests,
             TezosAccount account,
             int headOffset = 0,
             CancellationToken cancellationToken = default)
        {
            return Task.Run<(TezosOperation operation, Error error)>(async () =>
            {
                var from = operationsRequests.First().From;

                using var securePublicKey = await account
                    .GetPublicKeyAsync(from, cancellationToken)
                    .ConfigureAwait(false);

                using var publicKey = securePublicKey.ToUnmanagedBytes();

                var tezosConfig = account.Configuration;

                var api = new TezosApi(
                    settings: tezosConfig.ApiSettings,
                    logger: account.Logger);

                // get header
                var (currentHeaderHash, currentHeaderError) = await api
                    .GetHeaderAsync(offset: 0, cancellationToken)
                    .ConfigureAwait(false);

                if (currentHeaderError != null)
                    return (operation: null, error: currentHeaderError);

                // check revealed status
                var (isRevealed, isRevealedError) = await api
                    .IsRevealedAsync(from, cancellationToken)
                    .ConfigureAwait(false);

                if (isRevealedError != null)
                    return (operation: null, error: isRevealedError);

                // get operations counter
                var (counterFromNetwork, counterError) = await api
                    .GetCounterAsync(from, cancellationToken)
                    .ConfigureAwait(false);

                if (counterError != null)
                    return (operation: null, error: counterError);

                var counter = counterFromNetwork.Value;

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
                        Counter      = counter
                    });

                    counter++;
                }

                foreach (var request in operationsRequests)
                {
                    request.Content.Counter = counter++;

                    operations.Add(request.Content);
                }

                var (headerHash, headerError) = headOffset != 0
                    ? await api
                        .GetHeaderAsync(offset: headOffset, cancellationToken)
                        .ConfigureAwait(false)
                    : (currentHeaderHash, null);

                if (headerError != null)
                    return (operation: null, error: headerError);

                var needAutoFill = operationsRequests.Any(opr => 
                    (opr.Fee != null && opr.Fee.UseNetwork) ||
                    (opr.GasLimit != null && opr.GasLimit.UseNetwork) ||
                    (opr.StorageLimit != null && opr.StorageLimit.UseNetwork));

                if (needAutoFill)
                {
                    var autoFillError = await AutoFillAsync(
                            requests: operationsRequests,
                            blockHash: headerHash,
                            chainId: tezosConfig.ChainId,
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

        public static async Task<Error> AutoFillAsync(
            IEnumerable<TezosOperationParameters> requests,
            string blockHash,
            string chainId,
            ITezosApi api,
            TezosConfig tezosConfig,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var operations = requests.Select(r => r.Content);

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
                        return new Error(
                            code: Errors.AutoFillError,
                            description: "At least one of the operations is not applied");

                    var counter = result["counter"]?.ToString() ?? null;

                    if (counter == null)
                        continue;

                    var request = requests.FirstOrDefault(r =>
                        r.Content is ManagerOperationContent managedOperationContent &&
                        managedOperationContent.Counter.ToString() == counter);

                    if (request == null)
                        return new Error(
                            code: Errors.AutoFillError,
                            description: $"Can't find request with managed operation content and counter {counter}");

                    var operation = request.Content as ManagerOperationContent;

                    // gas limit
                    if (request.GasLimit == null || request.GasLimit.UseNetwork)
                    {
                        var gas = tezosConfig.ReserveGasLimit
                            + operationResult?["consumed_gas"]?.Value<int>() ?? 0;

                        gas += metaData
                            ?["internal_operation_results"]
                            ?.Sum(res => res["result"]?["consumed_gas"]?.Value<int>() ?? 0) ?? 0;

                        operation.GasLimit = gas;
                    }

                    // storage limit
                    if (request.StorageLimit == null || request.StorageLimit.UseNetwork)
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

                        operation.StorageLimit = request.StorageLimit != null && request.StorageLimit.UseSafeValue
                            ? Math.Max(operation.StorageLimit, storageDiff)
                            : storageDiff;
                    }

                    // fee
                    if (request.Fee == null || request.Fee.UseNetwork)
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
    }
}