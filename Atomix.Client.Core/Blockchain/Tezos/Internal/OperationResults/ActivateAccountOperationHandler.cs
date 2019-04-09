using Newtonsoft.Json.Linq;

namespace Atomix.Blockchain.Tezos.Internal.OperationResults
{
    internal class ActivateAccountOperationHandler : IOperationHandler
    {
        public string HandlesOperation => OperationType.ActivateAccount;

        public OperationResult ParseApplyOperationsResult(JToken appliedOp)
        {
            var result = new ActivateAccountOperationResult(appliedOp);

            var opResult = appliedOp["metadata"]?["balance_updates"];
            var change = opResult?.First["change"]?.ToString();
            if (change != null)
            {
                result.Change = decimal.Parse(change);
                result.Succeeded = true;
            }

            return result;
        }
    }
}