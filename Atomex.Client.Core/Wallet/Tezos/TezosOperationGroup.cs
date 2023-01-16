using System;
using System.Collections.Generic;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Wallets.Tezos.Common;

namespace Atomex.Wallets.Tezos
{
    public class TezosOperationGroup
    {
        public TezosOperationGroup(IEnumerable<TezosOperationParameters> operationsParameters)
        {
            OperationsParameters = operationsParameters ?? throw new ArgumentNullException(nameof(operationsParameters));
            CompletionEvent = new ManualResetEventAsync(isSet: false);
        }

        public IEnumerable<TezosOperationParameters> OperationsParameters { get; }
        public ManualResetEventAsync CompletionEvent { get; }
        public TezosOperation Operation { get; private set; }
        public Error? Error { get; private set; }

        public void CompleteWithOperation(TezosOperation operation)
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