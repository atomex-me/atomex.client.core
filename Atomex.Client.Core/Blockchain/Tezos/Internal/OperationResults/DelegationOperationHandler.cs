using Newtonsoft.Json.Linq;

namespace Atomex.Blockchain.Tezos.Internal.OperationResults
{
    internal class DelegationOperationHandler : IOperationHandler
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
            result.ConsumedGas = opResult?["consumed_milligas"]?.ToString() ?? result.ConsumedGas;
            result.Succeeded = result.Status == "applied";

            return result;
        }
    }
}