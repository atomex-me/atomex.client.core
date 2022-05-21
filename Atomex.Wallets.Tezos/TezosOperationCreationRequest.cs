using Netezos.Forging.Models;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Wallets.Tezos.Common;

namespace Atomex.Wallets.Tezos
{
    public class TezosOperationCreationRequest
    {
        public OperationContent Content { get; set; }

        public string From { get; set; }
        public Fee Fee { get; set; }
        public GasLimit GasLimit { get; set; }
        public StorageLimit StorageLimit { get; set; }
        public Counter Counter { get; set; }
        public bool IsFinal { get; set; }

        public TezosOperation Operation { get; set; }
        public Error Error { get; set; }
        public ManualResetEventAsync CompletionEvent { get; set; }

        public void Complete(TezosOperation operation)
        {
            Operation = operation;
            CompletionEvent.Set();
        }

        public void CompleteWithError(Error error)
        {
            Error = error;
            CompletionEvent.Set();
        }
    }
}