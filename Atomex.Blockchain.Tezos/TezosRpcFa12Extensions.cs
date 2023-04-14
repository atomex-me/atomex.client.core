using System;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Netezos.Encoding;
using Netezos.Forging.Models;

using Atomex.Blockchain.Tezos.Common;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos
{
    public static class TezosRpcFa12Extensions
    {
        private const int GetAllowanceGasLimit = 100000;

        public static async Task<Result<BigInteger>> GetFa12AllowanceAsync(
            this TezosRpc rpc,
            string holderAddress,
            string spenderAddress,
            string callingAddress,
            string tokenContractAddress,
            string tokenViewContractAddress,
            byte[] publicKey,
            TezosFillOperationSettings settings,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (request, error) = await rpc
                    .FillOperationAsync(
                        operationsRequests: new TezosOperationParameters[]
                        {
                            new TezosOperationParameters
                            {
                                Content = new TransactionContent
                                {
                                    Source       = callingAddress,
                                    Destination  = tokenContractAddress,
                                    Fee          = 0,
                                    Amount       = 0,
                                    GasLimit     = GetAllowanceGasLimit,
                                    StorageLimit = 0,
                                    Parameters = new Parameters
                                    {
                                        Entrypoint = "getAllowance",
                                        Value = Micheline.FromJson($"{{\"prim\":\"Pair\",\"args\":[{{\"prim\":\"Pair\",\"args\":[{{\"string\":\"{holderAddress}\"}},{{\"string\":\"{spenderAddress}\"}}]}},{{\"string\":\"{tokenViewContractAddress}%viewNat\"}}]}}")
                                    }
                                }
                            }
                        },
                        publicKey: publicKey,
                        settings: settings,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return error;

                var operations = JsonSerializer.Serialize(request!.OperationsContents);

                var runResultJson = await rpc
                    .RunOperationsAsync(
                        branch: request.Branch,
                        chainId: settings.ChainId,
                        operations: operations)
                    .ConfigureAwait(false);

                var runResult = JsonSerializer.Deserialize<JsonElement>(runResultJson);

                var valueElement = runResult
                    .Get("contents")
                    ?.LastOrDefault()
                    ?.Get("metadata")
                    ?.Get("internal_operation_results")
                    ?.Get(0)
                    ?.Get("result")
                    ?.Get("errors")
                    ?.Get(1)
                    ?.Get("with")
                    ?.Get("args")
                    ?.Get(0)
                    ?.Get("args")
                    ?.Get(0)
                    ?.Get("int");

                return valueElement != null && BigInteger.TryParse(valueElement.Value.GetString(), out var value)
                    ? value
                    : 0;
            }
            catch (Exception e)
            {
                return new Error(Errors.GetFa12AllowanceError, e.Message);
            }
        }
    
        public static async Task<Result<BigInteger>> GetFa12TotalSupply(
            this TezosRpc rpc,
            string callingAddress,
            string tokenContractAddress,
            string tokenViewContractAddress,
            byte[] publicKey,
            TezosFillOperationSettings settings,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (request, error) = await rpc
                    .FillOperationAsync(
                        operationsRequests: new TezosOperationParameters[]
                        {
                            new TezosOperationParameters
                            {
                                Content = new TransactionContent
                                {
                                    Source       = callingAddress,
                                    Destination  = tokenContractAddress,
                                    Fee          = 0,
                                    Amount       = 0,
                                    GasLimit     = GetAllowanceGasLimit,
                                    StorageLimit = 0,
                                    Parameters = new Parameters
                                    {
                                        Entrypoint = "getTotalSupply",
                                        Value = Micheline.FromJson($"{{\"prim\":\"Pair\",\"args\":[{{\"prim\":\"Unit\"}},{{\"string\":\"{tokenViewContractAddress}%viewNat\"}}]}}")
                                    }
                                }
                            }
                        },
                        publicKey: publicKey,
                        settings: settings,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return error;

                var operations = JsonSerializer.Serialize(request!.OperationsContents);

                var runResultJson = await rpc
                    .RunOperationsAsync(
                        branch: request.Branch,
                        chainId: settings.ChainId,
                        operations: operations)
                    .ConfigureAwait(false);

                var runResult = JsonSerializer.Deserialize<JsonElement>(runResultJson);

                var valueElement = runResult
                    .Get("contents")
                    ?.LastOrDefault()
                    ?.Get("metadata")
                    ?.Get("internal_operation_results")
                    ?.Get(0)
                    ?.Get("result")
                    ?.Get("errors")
                    ?.Get(1)
                    ?.Get("with")
                    ?.Get("args")
                    ?.Get(0)
                    ?.Get("args")
                    ?.Get(0)
                    ?.Get("int");

                return valueElement != null && BigInteger.TryParse(valueElement.Value.GetString(), out var value)
                    ? value
                    : 0;
            }
            catch (Exception e)
            {
                return new Error(Errors.GetFa12AllowanceError, e.Message);
            }
        }
    }
}