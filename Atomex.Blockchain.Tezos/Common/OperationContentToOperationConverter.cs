using Atomex.Blockchain.Tezos.Tzkt.Operations;
using Netezos.Forging.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Atomex.Blockchain.Tezos.Common
{
    public class OperationContentToOperationConverter
    {
        public Operation Convert(OperationContent operationContent, string id)
        {
            return operationContent switch
            {
                ActivationContent c => new ActivationOperation
                {
                    Hash = id,
                },
                BallotContent c => new BallotOperation {  },
                DelegationContent => new DelegationOperation { },
                DoubleBakingContent => new DoubleBakingOperation { },
                DoubleEndorsementContent => new DoubleEndorsingOperation { },
                DoublePreendorsementContent => new D,
                DrainDelegateContent => null,
                EndorsementContent => null,
                FailingNoopContent => null,
                IncreasePaidStorageContent => null,
                OriginationContent => null,
                PreendorsementContent => ,
                ProposalsContent => null,
                RegisterConstantContent => null,
                RevealContent => null,
                SeedNonceRevelationContent => null,
                SetDepositsLimitContent => null,
                TransactionContent => null,
                TransferTicketContent => null,
                TxRollupCommitContent => null,
                TxRollupDispatchTicketsContent => null,
                TxRollupFinalizeCommitmentContent => null,
                TxRollupOriginationContent => null,
                TxRollupRejectionContent => null,
                TxRollupRemoveCommitmentContent => null,
                TxRollupReturnBondContent => null,
                TxRollupSubmitBatchContent => null,
                UpdateConsensusKeyContent => null,
                VdfRevelationContent => null,
            };
        }
    }
}