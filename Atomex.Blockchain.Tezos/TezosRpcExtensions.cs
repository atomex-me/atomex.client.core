using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Netezos.Encoding;
using Netezos.Forging.Models;

using Atomex.Blockchain.Tezos.Common;
using Atomex.Common;
using Atomex.Wallets.Tezos;

namespace Atomex.Blockchain.Tezos
{
    public static class TezosRpcExtensions
    {
        private const int GetAllowanceGasLimit = 100000;

        public static async Task<Result<decimal>> GetFa12AllowanceAsync(
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
                                    Value = Micheline.FromJson($"{{'args':[{{'args':[{{'string':'{holderAddress}'}},{{'string':'{spenderAddress}'}}],'prim':'Pair'}},{{'string':'{tokenViewContractAddress}%viewNat'}}],'prim':'Pair'}}")
                                }
                            },
                            From         = callingAddress,
                            Fee          = Fee.FromValue(0),
                            GasLimit     = GasLimit.FromValue(GetAllowanceGasLimit),
                            StorageLimit = StorageLimit.FromValue(0)
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

                return valueElement != null && valueElement.Value.TryGetDecimal(out var value)
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