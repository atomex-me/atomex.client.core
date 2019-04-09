using Newtonsoft.Json.Linq;

namespace Atomix.Blockchain.Tezos.Internal.OperationResults
{
    internal class TransactionOperationHandler : IOperationHandler
    {
        public string HandlesOperation => OperationType.Transaction;

        public OperationResult ParseApplyOperationsResult(JToken appliedOp)
        {
            var result = new SendTransactionOperationResult(appliedOp);

            var opResult = appliedOp["metadata"]?["operation_result"];
            /*
            if (opResult?["status"]?.ToString() == "failed")
            {
                foreach (JObject error in opResult["errors"])
                {
                    error["contractCode"] = "";
                }
            }
            //*/
            result.Status = opResult?["status"]?.ToString() ?? result.Status;
            result.ConsumedGas = opResult?["consumed_gas"]?.ToString() ?? result.ConsumedGas;
            result.Succeeded = result.Status == "applied";

            return result;
        }
    }
}