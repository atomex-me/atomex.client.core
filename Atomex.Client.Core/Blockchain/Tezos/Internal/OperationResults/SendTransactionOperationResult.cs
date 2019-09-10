using Newtonsoft.Json.Linq;

namespace Atomex.Blockchain.Tezos.Internal.OperationResults
{
    public class SendTransactionOperationResult : OperationResult
    {
        public SendTransactionOperationResult()
        { }

        public SendTransactionOperationResult(JToken data)
            : base(data)
        { }

        public string Status { get; internal set; } = "unknown";
        public string ConsumedGas { get; internal set; } = "0";
    }
}