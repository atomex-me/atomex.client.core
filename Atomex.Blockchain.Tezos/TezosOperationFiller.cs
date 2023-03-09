using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Netezos.Forging;
using Netezos.Forging.Models;
using Newtonsoft.Json.Linq;

using Atomex.Blockchain.Tezos.Common;
using Atomex.Common;
using Atomex.Cryptography;

namespace Atomex.Blockchain.Tezos
{
    public class TezosFillOperationSettings
    {
        public string ChainId { get; set; } = "NetXdQprcVkpaWU";
        public int RevealGasLimit { get; set; } = 1200;
        public int ReserveGasLimit { get; set; } = 100;
        public int ActivationStorageLimit { get; set; } = 257;
        public int MinimalFee { get; set; } = 100;
        public decimal MinimalNanotezPerGasUnit { get; set; } = 0.1m;
        public decimal MinimalNanotezPerByte { get; set; } = 1;
        public int HeadSizeInBytes { get; set; } = 32;
        public int SignatureSizeInBytes { get; set; } = 64;
    }

    public static class TezosOperationFiller
    {
        public static async Task<Result<TezosOperationRequest>> FillOperationAsync(
            this TezosRpc rpc,
            IEnumerable<TezosOperationParameters> operationsRequests,
            byte[] publicKey,
            TezosFillOperationSettings settings,
            int headOffset = 0,
            CancellationToken cancellationToken = default)
        {
            var from = operationsRequests.First().From;

            // get header
            var currentHeader = await rpc
                .GetHeaderAsync(offset: 0, cancellationToken)
                .ConfigureAwait(false);

            // check revealed status
            var managerKeyJson = await rpc
                .GetManagerKeyAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var managerKey = JsonSerializer.Deserialize<JsonElement>(managerKeyJson);
            var isRevealed = managerKey.ValueKind != JsonValueKind.Null;

            // get operations counter
            var accountInfo = await rpc
                .GetAccountAsync(from, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var counter = int.Parse(accountInfo.Counter) + 1;

            var operations = new List<ManagerOperationContent>();

            if (!isRevealed)
            {
                operations.Add(new RevealContent
                {
                    Source       = from,
                    PublicKey    = Base58Check.Encode(publicKey, TezosPrefix.Edpk),
                    StorageLimit = 0,
                    GasLimit     = settings.RevealGasLimit,
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

            var header = headOffset != 0
                ? await rpc
                    .GetHeaderAsync(offset: headOffset, cancellationToken)
                    .ConfigureAwait(false)
                : currentHeader;

            var needAutoFill = operationsRequests.Any(opr =>
                opr.Fee != null && opr.Fee.UseNetwork ||
                opr.GasLimit != null && opr.GasLimit.UseNetwork ||
                opr.StorageLimit != null && opr.StorageLimit.UseNetwork);

            var isAutoFilled = false;

            if (needAutoFill)
            {
                var (isSuccess, error) = await rpc
                    .AutoFillAsync(
                        requests: operationsRequests,
                        blockHash: header.Hash,
                        settings: settings,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                isAutoFilled = error == null && isSuccess;
            }

            return new TezosOperationRequest(
                operationsContents: operations,
                branch: header.Hash,
                isAutoFilled: isAutoFilled);
        }

        public static async Task<Result<bool>> AutoFillAsync(
            this TezosRpc rpc,
            IEnumerable<TezosOperationParameters> requests,
            string blockHash,
            TezosFillOperationSettings settings,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var operations = requests
                    .Select(r => r.Content)
                    .ToList();

                var serialziedOperations = JsonSerializer.Serialize(operations, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

                var runJsonResponse = await rpc
                    .RunOperationsAsync(
                        branch: blockHash,
                        chainId: settings.ChainId,
                        operations: serialziedOperations,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var runResponse = JObject.Parse(runJsonResponse); // todo: use System.Text.Json instead

                foreach (var result in runResponse["contents"])
                {
                    var metaData = result["metadata"];
                    var operationResult = metaData?["operation_result"];

                    if (operationResult?["status"]?.ToString() != "applied")
                        return new Error(
                            code: Errors.AutoFillError,
                            message: "At least one of the operations is not applied");

                    var counter = result["counter"]?.ToString() ?? null;

                    if (counter == null)
                        continue;

                    var request = requests.FirstOrDefault(r =>
                        r.Content is ManagerOperationContent managedOperationContent &&
                        managedOperationContent.Counter.ToString() == counter);

                    if (request == null)
                        return new Error(
                            code: Errors.AutoFillError,
                            message: $"Can't find request with managed operation content and counter {counter}");

                    var operation = request.Content;

                    // gas limit
                    if (request.GasLimit == null || request.GasLimit.UseNetwork)
                    {
                        var gas = settings.ReserveGasLimit
                            + (operationResult?["consumed_milligas"]?.Value<int>() ?? 0) / 1000;

                        gas += metaData
                            ?["internal_operation_results"]
                            ?.Sum(res => (res["result"]?["consumed_gas"]?.Value<int>() ?? 0) / 1000) ?? 0;

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

                        storageDiff += settings.ActivationStorageLimit * isAllocatedContract;

                        storageDiff += settings.ActivationStorageLimit * metaData
                            ?["internal_operation_results"]
                            ?.Where(res => res["result"]?["allocated_destination_contract"]?.ToString() == "True" ||
                                           res["kind"]?.ToString() == "origination")
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
                            + Math.Ceiling((settings.HeadSizeInBytes + settings.SignatureSizeInBytes) / (decimal)operations.Count());

                        operation.Fee = (long)(settings.MinimalFee
                            + settings.MinimalNanotezPerByte * size
                            + Math.Ceiling(settings.MinimalNanotezPerGasUnit * operation.GasLimit))
                            + 10;
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                return new Error(Errors.AutoFillError, e.Message);
            }
        }
    }
}