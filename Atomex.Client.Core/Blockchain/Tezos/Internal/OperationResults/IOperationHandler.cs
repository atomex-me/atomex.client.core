using Newtonsoft.Json.Linq;

namespace Atomex.Blockchain.Tezos.Internal.OperationResults
{
    internal interface IOperationHandler
    {
        string HandlesOperation { get; }
        OperationResult ParseApplyOperationsResult(JToken appliedOp);
    }
}