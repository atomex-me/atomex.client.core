using Newtonsoft.Json.Linq;

namespace Atomix.Blockchain.Tezos.Internal.OperationResults
{
    public class ActivateAccountOperationResult : OperationResult
    {
        public ActivateAccountOperationResult()
        { }

        public ActivateAccountOperationResult(JToken data)
            : base(data)
        { }

        public decimal Change { get; internal set; }
    }
}