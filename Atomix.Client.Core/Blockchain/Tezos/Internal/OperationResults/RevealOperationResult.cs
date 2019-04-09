using Newtonsoft.Json.Linq;

namespace Atomix.Blockchain.Tezos.Internal.OperationResults
{
    public class RevealOperationResult : OperationResult
    {
        public RevealOperationResult()
        { }

        public RevealOperationResult(JToken data)
            : base(data)
        { }
    }
}
