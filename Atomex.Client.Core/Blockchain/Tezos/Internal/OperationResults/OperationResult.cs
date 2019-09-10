using Newtonsoft.Json.Linq;

namespace Atomex.Blockchain.Tezos.Internal.OperationResults
{
    public class OperationResult
    {
        public OperationResult()
        { }

        public OperationResult(JToken data)
        {
            Data = data;
        }

        public JToken Data { get; internal set; }
        public bool Succeeded { get; internal set; }
    }
}