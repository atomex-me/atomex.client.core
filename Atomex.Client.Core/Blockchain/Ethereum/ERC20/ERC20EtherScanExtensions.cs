using System;

using Nethereum.Hex.HexTypes;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.EthereumTokens;

namespace Atomex.Blockchain.Ethereum.ERC20
{
    public static class ERC20EtherScanExtensions
    {
        private const int PrefixOffset = 2;
        private const int AddressLengthInHex = 40;
        private const int TopicSizeInHex = 64;
        private const int InputItemSizeInHex = 64;
        //private const int SignatureLengthInHex = 32;
        
        public static bool IsERC20SignatureEqual(this EthereumTransaction_OLD transaction, string signatureHash)
        {
            var txSignature = transaction.Input.Substring(0, transaction.Input.Length % InputItemSizeInHex);

            return signatureHash.StartsWith(txSignature);
        }

        public static bool IsERC20ApproveTransaction(this EthereumTransaction_OLD transaction)
        {
            return transaction.IsERC20SignatureEqual(FunctionSignatureExtractor.GetSignatureHash<ERC20ApproveFunctionMessage>());
        }

        public static bool IsERC20TransferTransaction(this EthereumTransaction_OLD transaction)
        {
            return transaction.IsERC20SignatureEqual(FunctionSignatureExtractor.GetSignatureHash<ERC20TransferFunctionMessage>());
        }

        public static bool IsERC20InitiateTransaction(this EthereumTransaction_OLD transaction)
        {
            return transaction.IsERC20SignatureEqual(FunctionSignatureExtractor.GetSignatureHash<ERC20InitiateFunctionMessage>());
        }

        public static bool IsERC20AddTransaction(this EthereumTransaction_OLD transaction)
        {
            return transaction.IsERC20SignatureEqual(FunctionSignatureExtractor.GetSignatureHash<ERC20AddFunctionMessage>());
        }

        public static bool IsERC20RedeemTransaction(this EthereumTransaction_OLD transaction)
        {
            return transaction.IsERC20SignatureEqual(FunctionSignatureExtractor.GetSignatureHash<ERC20RedeemFunctionMessage>());
        }

        public static bool IsERC20RefundTransaction(this EthereumTransaction_OLD transaction)
        {
            return transaction.IsERC20SignatureEqual(FunctionSignatureExtractor.GetSignatureHash<ERC20RefundFunctionMessage>());
        }
        
        public static bool IsERC20ApprovalEvent(this EtherScanApi.ContractEvent contractEvent)
        {
            return contractEvent.EventSignatureHash() == EventSignatureExtractor.GetSignatureHash<ERC20ApprovalEventDTO>();
        }

        public static bool IsERC20TransferEvent(this EtherScanApi.ContractEvent contractEvent)
        {
            return contractEvent.EventSignatureHash() == EventSignatureExtractor.GetSignatureHash<ERC20TransferEventDTO>();
        }

        public static bool IsERC20InitiatedEvent(this EtherScanApi.ContractEvent contractEvent)
        {
            return contractEvent.EventSignatureHash() == EventSignatureExtractor.GetSignatureHash<ERC20InitiatedEventDTO>();
        }

        public static bool IsERC20AddedEvent(this EtherScanApi.ContractEvent contractEvent)
        {
            return contractEvent.EventSignatureHash() == EventSignatureExtractor.GetSignatureHash<ERC20AddedEventDTO>();
        }

        public static bool IsERC20RedeemedEvent(this EtherScanApi.ContractEvent contractEvent)
        {
            return contractEvent.EventSignatureHash() == EventSignatureExtractor.GetSignatureHash<ERC20RedeemedEventDTO>();
        }

        public static bool IsERC20RefundedEvent(this EtherScanApi.ContractEvent contractEvent)
        {
            return contractEvent.EventSignatureHash() == EventSignatureExtractor.GetSignatureHash<ERC20RefundedEventDTO>();
        }

        public static EthereumTransaction_OLD ParseERC20TransactionType(
            this EthereumTransaction_OLD transaction)
        {
            if (transaction.Input == "0x")
                return transaction;

            if (transaction.Currency == "ETH")
            {
                if (transaction.IsERC20TransferTransaction() ||
                    transaction.IsERC20ApproveTransaction())
                    transaction.Type |= BlockchainTransactionType.TokenCall;
                else if (transaction.IsERC20InitiateTransaction() ||
                    transaction.IsERC20RedeemTransaction() ||
                    transaction.IsERC20RefundTransaction())
                    transaction.Type |= BlockchainTransactionType.SwapCall;
            }

            return transaction;
        }

        public static EthereumTransaction_OLD ParseERC20Input(
            this EthereumTransaction_OLD transaction)
        {
            if (transaction.Input == "0x")
                return transaction;

            if (transaction.IsERC20TransferTransaction())
                return transaction.ParseERC20TransferInput();
            else if (transaction.IsERC20InitiateTransaction())
                return transaction.ParseERC20InitiateInput();
            else if (transaction.IsERC20AddTransaction())
                return transaction.ParseERC20AddInput();

            return transaction;
        }
        
        public static EthereumTransaction_OLD ParseERC20TransferInput(
            this EthereumTransaction_OLD transaction)
        {
            var input = transaction.Input.Substring(transaction.Input.Length % InputItemSizeInHex);
            
            transaction.To = $"0x{input.Substring(InputItemSizeInHex - AddressLengthInHex, AddressLengthInHex)}";
            transaction.Amount = new HexBigInteger(input.Substring(InputItemSizeInHex, InputItemSizeInHex)).Value;

            return transaction;
        }

        public static EthereumTransaction_OLD ParseERC20InitiateInput(
            this EthereumTransaction_OLD transaction)
        {
            var input = transaction.Input.Substring(transaction.Input.Length % InputItemSizeInHex);

            transaction.Amount = new HexBigInteger(input.Substring(InputItemSizeInHex * 5, InputItemSizeInHex)).Value;

            return transaction;
        }

        public static EthereumTransaction_OLD ParseERC20AddInput(
            this EthereumTransaction_OLD transaction)
        {
            var input = transaction.Input.Substring(transaction.Input.Length % InputItemSizeInHex);

            transaction.Amount = new HexBigInteger(input.Substring(InputItemSizeInHex * 1, InputItemSizeInHex)).Value;

            return transaction;
        }

        public static ERC20ApprovalEventDTO ParseERC20ApprovalEvent(
            this EtherScanApi.ContractEvent contractEvent)
        {
            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<ERC20ApprovalEventDTO>();

            if (contractEvent.Topics == null ||
                contractEvent.Topics.Count != 3 ||
                contractEvent.EventSignatureHash() != eventSignatureHash)
                throw new Exception("Invalid ERC20 token contract event");

            var valueHex = contractEvent.HexData.Substring(PrefixOffset, TopicSizeInHex);

            return new ERC20ApprovalEventDTO
            {
                Owner = 
                    $"0x{contractEvent.Topics[1].Substring(contractEvent.Topics[1].Length - AddressLengthInHex)}",
                Spender =
                    $"0x{contractEvent.Topics[2].Substring(contractEvent.Topics[2].Length - AddressLengthInHex)}",
                Value = new HexBigInteger(valueHex).Value
            };
        }

        public static ERC20TransferEventDTO ParseERC20TransferEvent(
            this EtherScanApi.ContractEvent contractEvent)
        {
            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<ERC20TransferEventDTO>();

            if (contractEvent.Topics == null ||
                contractEvent.Topics.Count != 3 ||
                contractEvent.EventSignatureHash() != eventSignatureHash)
                throw new Exception("Invalid ERC20 token contract event");

            var valueHex = contractEvent.HexData.Substring(PrefixOffset, TopicSizeInHex);

            return new ERC20TransferEventDTO
            {
                From =
                    $"0x{contractEvent.Topics[1].Substring(contractEvent.Topics[1].Length - AddressLengthInHex)}",
                To =
                    $"0x{contractEvent.Topics[2].Substring(contractEvent.Topics[2].Length - AddressLengthInHex)}",
                Value = new HexBigInteger(valueHex).Value
            };
        }

        public static ERC20InitiatedEventDTO ParseERC20InitiatedEvent(
            this EtherScanApi.ContractEvent contractEvent)
        {
            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<ERC20InitiatedEventDTO>();

            if (contractEvent.Topics == null ||
                contractEvent.Topics.Count != 4 ||
                contractEvent.EventSignatureHash() != eventSignatureHash)
                throw new Exception("Invalid contract event");

            var initiatorHex = contractEvent.HexData.Substring(PrefixOffset, TopicSizeInHex);
            var refundTimeHex = contractEvent.HexData.Substring(PrefixOffset + TopicSizeInHex, TopicSizeInHex);
            var countdownHex = contractEvent.HexData.Substring(PrefixOffset + 2 * TopicSizeInHex, TopicSizeInHex);
            var valueHex = contractEvent.HexData.Substring(PrefixOffset + 3 * TopicSizeInHex, TopicSizeInHex);
            var redeemFeeHex = contractEvent.HexData.Substring(PrefixOffset + 4 * TopicSizeInHex, TopicSizeInHex);
            var active = contractEvent.HexData.Substring(PrefixOffset + 5 * TopicSizeInHex, TopicSizeInHex);

            return new ERC20InitiatedEventDTO
            {
                HashedSecret = Hex.FromString(contractEvent.Topics[1], true),
                ERC20Contract =
                    $"0x{contractEvent.Topics[2].Substring(contractEvent.Topics[2].Length - AddressLengthInHex)}",
                Participant =
                    $"0x{contractEvent.Topics[3].Substring(contractEvent.Topics[3].Length - AddressLengthInHex)}",
                Initiator =
                    $"0x{initiatorHex.Substring(initiatorHex.Length - AddressLengthInHex)}",
                RefundTimestamp = new HexBigInteger(refundTimeHex).Value,
                Countdown = new HexBigInteger(countdownHex).Value,
                Value = new HexBigInteger(valueHex).Value,
                RedeemFee = new HexBigInteger(redeemFeeHex).Value,
                Active = new HexBigInteger(active).Value != 0
            };
        }

        public static ERC20AddedEventDTO ParseERC20AddedEvent(
            this EtherScanApi.ContractEvent contractEvent)
        {
            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<ERC20AddedEventDTO>();

            if (contractEvent.Topics == null ||
                contractEvent.Topics.Count != 2 ||
                contractEvent.EventSignatureHash() != eventSignatureHash)
                throw new Exception("Invalid contract event");

            var initiatorHex = contractEvent.HexData.Substring(PrefixOffset, TopicSizeInHex);
            var valueHex = contractEvent.HexData.Substring(PrefixOffset + TopicSizeInHex, TopicSizeInHex);

            return new ERC20AddedEventDTO
            {
                HashedSecret = Hex.FromString(contractEvent.Topics[1], true),
                Initiator = $"0x{initiatorHex.Substring(initiatorHex.Length - AddressLengthInHex)}",
                Value = new HexBigInteger(valueHex).Value
            };
        }

        public static ERC20RedeemedEventDTO ParseERC20RedeemedEvent(
            this EtherScanApi.ContractEvent contractEvent)
        {
            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<ERC20RedeemedEventDTO>();

            if (contractEvent.Topics == null ||
                contractEvent.Topics.Count != 2 ||
                contractEvent.EventSignatureHash() != eventSignatureHash)
                throw new Exception("Invalid contract event");

            return new ERC20RedeemedEventDTO
            {
                HashedSecret = Hex.FromString(contractEvent.Topics[1], true),
                Secret = Hex.FromString(contractEvent.HexData, true)
            };
        }

        public static ERC20RefundedEventDTO ParseRefundedEvent(
            this EtherScanApi.ContractEvent contractEvent)
        {
            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<ERC20RefundedEventDTO>();

            if (contractEvent.Topics == null ||
                contractEvent.Topics.Count != 2 ||
                contractEvent.EventSignatureHash() != eventSignatureHash)
                throw new Exception("Invalid contract event");

            return new ERC20RefundedEventDTO
            {
                HashedSecret = Hex.FromString(contractEvent.Topics[1], true),
            };
        }

        public static EthereumTransaction_OLD TransformApprovalEvent(
            this EtherScanApi.ContractEvent contractEvent,
            Erc20Config erc20,
            long lastBlockNumber)
        {
            if (!contractEvent.IsERC20ApprovalEvent())
                return null;

            var approvalEvent = contractEvent.ParseERC20ApprovalEvent();

            var tx = new EthereumTransaction_OLD() //todo: make a refactoring
            {
                Currency     = erc20.Name,
                Id           = contractEvent.HexTransactionHash,
                Type         = BlockchainTransactionType.Output | BlockchainTransactionType.TokenApprove,
                State        = BlockchainTransactionState.Confirmed, //todo: check if true in 100% cases
                CreationTime = contractEvent.HexTimeStamp.Substring(PrefixOffset).FromHexString(),

                From = approvalEvent.Owner,
                To   = approvalEvent.Spender,
                Amount = 0,
                ////Nonce 
                GasPrice = new HexBigInteger(contractEvent.HexGasPrice).Value,
                ////GasLimit
                GasLimit      = new HexBigInteger(contractEvent.HexGasUsed).Value,
                ReceiptStatus = true,
                IsInternal    = false,
                InternalIndex = 0,
                BlockInfo = new BlockInfo
                {
                    Confirmations = 1 + (int)(lastBlockNumber - long.Parse(contractEvent.HexBlockNumber.Substring(PrefixOffset), System.Globalization.NumberStyles.HexNumber)),
                    //    //Confirmations = txReceipt.Status != null
                    //    //? (int)txReceipt.Status.Value
                    //    //: 0,
                    //    //BlockHash = tx.BlockHash,
                    BlockHeight = long.Parse(contractEvent.HexBlockNumber.Substring(PrefixOffset), System.Globalization.NumberStyles.HexNumber),
                    //    //BlockTime = blockTimeStamp,
                    //    //FirstSeen = blockTimeStamp
                }
            };

            return tx;
        }

        public static EthereumTransaction_OLD TransformTransferEvent(
            this EtherScanApi.ContractEvent contractEvent,
            string address,
            EthereumTokens.Erc20Config erc20,
            long lastBlockNumber)
        {
            if (!contractEvent.IsERC20TransferEvent())
                return null;

            var transferEvent = contractEvent.ParseERC20TransferEvent();

            var tx = new EthereumTransaction_OLD() //todo: make a refactoring
            {
                Currency = erc20.Name,
                Id = contractEvent.HexTransactionHash,

                Type = transferEvent.From == address
                    ? transferEvent.To == erc20.SwapContractAddress.ToLowerInvariant()   //todo: change to erc20.SwapContractAddress after currencies.json update
                        ? BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment
                        : BlockchainTransactionType.Output
                    : BlockchainTransactionType.Input,  //todo: recognize redeem&refund
                State = BlockchainTransactionState.Confirmed, //todo: check if true in 100% cases
                CreationTime = contractEvent.HexTimeStamp.Substring(PrefixOffset).FromHexString(),

                From = transferEvent.From,
                To = transferEvent.To,
                Amount = transferEvent.Value.ToHexBigInteger(),
                ////Nonce 
                GasPrice = new HexBigInteger(contractEvent.HexGasPrice).Value,
                ////GasLimit
                GasLimit = new HexBigInteger(contractEvent.HexGasUsed).Value,
                ReceiptStatus = true,
                IsInternal = transferEvent.From == erc20.SwapContractAddress.ToLowerInvariant()
                    || transferEvent.To == erc20.SwapContractAddress.ToLowerInvariant(),
                InternalIndex = 0,
                BlockInfo = new BlockInfo
                {
                    Confirmations = 1 + (int)(lastBlockNumber - long.Parse(contractEvent.HexBlockNumber.Substring(PrefixOffset), System.Globalization.NumberStyles.HexNumber)), 
                //    //Confirmations = txReceipt.Status != null
                //    //? (int)txReceipt.Status.Value
                //    //: 0,
                //    //BlockHash = tx.BlockHash,
                    BlockHeight = long.Parse(contractEvent.HexBlockNumber.Substring(PrefixOffset), System.Globalization.NumberStyles.HexNumber),
                //    //BlockTime = blockTimeStamp,
                //    //FirstSeen = blockTimeStamp
                }
            };

            return tx;
        }
    }
}