using Newtonsoft.Json.Linq;

namespace Atomix.Blockchain.Tezos.Internal.OperationResults
{
    internal class RevealOperationHandler : IOperationHandler
    {
        public string HandlesOperation => OperationType.Reveal;

        public OperationResult ParseApplyOperationsResult(JToken appliedOp)
        {
            var result = new RevealOperationResult(appliedOp)
            {
                Succeeded = appliedOp["metadata"]?["operation_result"]?["status"]?.Value<string>() == "applied"
            };

            return result;
        }
    }
}