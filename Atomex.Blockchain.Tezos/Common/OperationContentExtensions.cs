using System.Text.Json;

using Netezos.Encoding;
using Netezos.Forging.Models;

using Atomex.Blockchain.Tezos.Tzkt.Operations;
using Operation = Atomex.Blockchain.Tezos.Tzkt.Operations.Operation;

namespace Atomex.Blockchain.Tezos.Common
{
    public static class OperationContentExtensions
    {
        public static Operation ToOperation(this OperationContent operationContent, string id)
        {
            return operationContent switch
            {
                TransactionContent c => new TransactionOperation
                {
                    Hash         = id,
                    Counter      = c.Counter,
                    BakerFee     = c.Fee,
                    GasLimit     = c.GasLimit,
                    StorageLimit = c.StorageLimit,
                    Sender       = new Alias { Address = c.Source },
                    Amount       = c.Amount,
                    Target       = new Alias { Address = c.Destination },
                    Parameter    = c.Parameters != null
                        ? new Parameter
                        {
                            Entrypoint = c.Parameters.Entrypoint,
                            Value = JsonDocument.Parse(c.Parameters.Value.ToJson()).RootElement
                        }
                        : null,
                },
                RevealContent c => new RevealOperation
                {
                     Hash         = id,
                     Counter      = c.Counter,
                     BakerFee     = c.Fee,
                     GasLimit     = c.GasLimit,
                     StorageLimit = c.StorageLimit,
                     Sender       = new Alias { Address = c.Source },
                },
                DelegationContent c => new DelegationOperation
                {
                    Hash         = id,
                    NewDelegate  = new Alias { Address = c.Delegate },
                    Counter      = c.Counter,
                    BakerFee     = c.Fee,
                    GasLimit     = c.GasLimit,
                    StorageLimit = c.StorageLimit,
                    Sender       = new Alias { Address = c.Source }
                },
                OriginationContent c => new OriginationOperation
                {
                    Hash             = id,
                    Counter          = c.Counter,
                    BakerFee         = c.Fee,
                    GasLimit         = c.GasLimit,
                    StorageLimit     = c.StorageLimit,
                    Sender           = new Alias { Address = c.Source },
                    ContractBalance  = c.Balance,
                    ContractDelegate = new Alias { Address = c.Delegate },
                },
                ActivationContent c => new ActivationOperation
                {
                    Hash   = id,
                    Sender = new Alias { Address = c.Address }
                },
                BallotContent c => new BallotOperation
                {
                    Hash     = id,
                    Delegate = new Alias { Address = c.Source },
                    //Period = new Period { },
                    Vote     = c.Ballot.ToString(),
                    Proposal = new ProposalAlias { Hash = c.Proposal }
                },
                // todo: use specifid operations
                DoubleBakingContent or
                DoubleEndorsementContent or
                DoublePreendorsementContent or
                DrainDelegateContent or
                EndorsementContent or
                FailingNoopContent or
                IncreasePaidStorageContent or
                PreendorsementContent or
                ProposalsContent or
                RegisterConstantContent or
                SeedNonceRevelationContent or
                SetDepositsLimitContent or
                TransferTicketContent or
                TxRollupCommitContent or
                TxRollupDispatchTicketsContent or
                TxRollupFinalizeCommitmentContent or
                TxRollupOriginationContent or
                TxRollupRejectionContent or
                TxRollupRemoveCommitmentContent or
                TxRollupReturnBondContent or
                TxRollupSubmitBatchContent or
                UpdateConsensusKeyContent or
                VdfRevelationContent or
                _ => new Operation
                {
                    Hash = id
                },
            };
        }
    }
}