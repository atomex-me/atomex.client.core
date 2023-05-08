#nullable enable

using Atomex.Blockchain.Tezos;
using Atomex.Common;

namespace Atomex.Wallets.Tezos
{
    public class TezosOperationRequestResult
    {
        public TezosOperationRequest Request { get; private set; }
        public string? OperationId { get; private set; }
        public Error? Error { get; private set; }

        private TezosOperationRequestResult(TezosOperationRequest request, string? operationId, Error? error)
        {
            Request = request;
            OperationId = operationId;
            Error = error;
        }

        public static TezosOperationRequestResult FromOperation(TezosOperationRequest request, string operationId)
        {
            return new TezosOperationRequestResult(request, operationId, error: null);
        }

        public static TezosOperationRequestResult FromError(TezosOperationRequest request, Error? error)
        {
            return new TezosOperationRequestResult(request, operationId: null, error);
        }
    }
}