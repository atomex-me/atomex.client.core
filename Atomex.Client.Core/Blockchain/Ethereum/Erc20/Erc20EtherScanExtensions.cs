using System;

using Nethereum.Hex.HexTypes;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.EthereumTokens;

namespace Atomex.Blockchain.Ethereum.Erc20
{
    public static class Erc20EtherScanExtensions
    {
        private const int PrefixOffset = 2;
        private const int AddressLengthInHex = 40;
        private const int TopicSizeInHex = 64;
        private const int InputItemSizeInHex = 64;
        //private const int SignatureLengthInHex = 32;
        
        //public static bool IsErc20ApproveTransaction(this EthereumTransaction transaction)
        //{
        //    return transaction.IsErc20SignatureEqual(FunctionSignatureExtractor.GetSignatureHash<Erc20ApproveFunctionMessage>());
        //}

        public static bool IsErc20TransferTransaction(this EthereumTransaction transaction)
        {
            return transaction.IsMethodCall(FunctionSignatureExtractor.GetSignatureHash<Erc20TransferFunctionMessage>());
        }

        public static bool IsErc20InitiateTransaction(this EthereumTransaction transaction)
        {
            return transaction.IsMethodCall(FunctionSignatureExtractor.GetSignatureHash<Erc20InitiateFunctionMessage>());
        }

        public static bool IsErc20AddTransaction(this EthereumTransaction transaction)
        {
            return transaction.IsMethodCall(FunctionSignatureExtractor.GetSignatureHash<Erc20AddFunctionMessage>());
        }

        //public static bool IsErc20RedeemTransaction(this EthereumTransaction transaction)
        //{
        //    return transaction.IsErc20SignatureEqual(FunctionSignatureExtractor.GetSignatureHash<Erc20RedeemFunctionMessage>());
        //}

        //public static bool IsErc20RefundTransaction(this EthereumTransaction transaction)
        //{
        //    return transaction.IsErc20SignatureEqual(FunctionSignatureExtractor.GetSignatureHash<Erc20RefundFunctionMessage>());
        //}
        
        public static bool IsErc20ApprovalEvent(this EtherScanApi.ContractEvent contractEvent)
        {
            return contractEvent.EventSignatureHash() == EventSignatureExtractor.GetSignatureHash<Erc20ApprovalEventDTO>();
        }

        public static bool IsErc20TransferEvent(this EtherScanApi.ContractEvent contractEvent)
        {
            return contractEvent.EventSignatureHash() == EventSignatureExtractor.GetSignatureHash<Erc20TransferEventDTO>();
        }

        //public static bool IsErc20InitiatedEvent(this EtherScanApi.ContractEvent contractEvent)
        //{
        //    return contractEvent.EventSignatureHash() == EventSignatureExtractor.GetSignatureHash<Erc20InitiatedEventDTO>();
        //}

        //public static bool IsErc20AddedEvent(this EtherScanApi.ContractEvent contractEvent)
        //{
        //    return contractEvent.EventSignatureHash() == EventSignatureExtractor.GetSignatureHash<Erc20AddedEventDTO>();
        //}

        //public static bool IsErc20RedeemedEvent(this EtherScanApi.ContractEvent contractEvent)
        //{
        //    return contractEvent.EventSignatureHash() == EventSignatureExtractor.GetSignatureHash<Erc20RedeemedEventDTO>();
        //}

        //public static bool IsErc20RefundedEvent(this EtherScanApi.ContractEvent contractEvent)
        //{
        //    return contractEvent.EventSignatureHash() == EventSignatureExtractor.GetSignatureHash<Erc20RefundedEventDTO>();
        //}

        //public static EthereumTransaction ParseErc20TransactionType(
        //    this EthereumTransaction transaction)
        //{
        //    if (transaction.Input == "0x")
        //        return transaction;

        //    if (transaction.Currency == "ETH")
        //    {
        //        if (transaction.IsErc20TransferTransaction() ||
        //            transaction.IsErc20ApproveTransaction())
        //            transaction.Type |= BlockchainTransactionType.TokenCall;
        //        else if (transaction.IsErc20InitiateTransaction() ||
        //            transaction.IsErc20RedeemTransaction() ||
        //            transaction.IsErc20RefundTransaction())
        //            transaction.Type |= BlockchainTransactionType.ContractCall;
        //    }

        //    return transaction;
        //}

        public static EthereumTransaction ParseErc20Input(
            this EthereumTransaction transaction)
        {
            if (transaction.Input == "0x")
                return transaction;

            if (transaction.IsErc20TransferTransaction())
                return transaction.ParseErc20TransferInput();
            else if (transaction.IsErc20InitiateTransaction())
                return transaction.ParseErc20InitiateInput();
            else if (transaction.IsErc20AddTransaction())
                return transaction.ParseErc20AddInput();

            return transaction;
        }
        
        public static EthereumTransaction ParseErc20TransferInput(
            this EthereumTransaction transaction)
        {
            var input = transaction.Input[(transaction.Input.Length % InputItemSizeInHex)..];
            
            transaction.To = $"0x{input.Substring(InputItemSizeInHex - AddressLengthInHex, AddressLengthInHex)}";
            transaction.Amount = new HexBigInteger(input.Substring(InputItemSizeInHex, InputItemSizeInHex)).Value;

            return transaction;
        }

        public static EthereumTransaction ParseErc20InitiateInput(
            this EthereumTransaction transaction)
        {
            var input = transaction.Input[(transaction.Input.Length % InputItemSizeInHex)..];

            transaction.Amount = new HexBigInteger(input.Substring(InputItemSizeInHex * 5, InputItemSizeInHex)).Value;

            return transaction;
        }

        public static EthereumTransaction ParseErc20AddInput(
            this EthereumTransaction transaction)
        {
            var input = transaction.Input[(transaction.Input.Length % InputItemSizeInHex)..];

            transaction.Amount = new HexBigInteger(input.Substring(InputItemSizeInHex * 1, InputItemSizeInHex)).Value;

            return transaction;
        }

        public static Erc20ApprovalEventDTO ParseErc20ApprovalEvent(
            this EtherScanApi.ContractEvent contractEvent)
        {
            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<Erc20ApprovalEventDTO>();

            if (contractEvent.Topics == null ||
                contractEvent.Topics.Count != 3 ||
                contractEvent.EventSignatureHash() != eventSignatureHash)
                throw new Exception("Invalid ERC20 token contract event");

            var valueHex = contractEvent.HexData.Substring(PrefixOffset, TopicSizeInHex);

            return new Erc20ApprovalEventDTO
            {
                Owner = $"0x{contractEvent.Topics[1][^AddressLengthInHex..]}",
                Spender = $"0x{contractEvent.Topics[2][^AddressLengthInHex..]}",
                Value = new HexBigInteger(valueHex).Value
            };
        }

        public static Erc20TransferEventDTO ParseErc20TransferEvent(
            this EtherScanApi.ContractEvent contractEvent)
        {
            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<Erc20TransferEventDTO>();

            if (contractEvent.Topics == null ||
                contractEvent.Topics.Count != 3 ||
                contractEvent.EventSignatureHash() != eventSignatureHash)
                throw new Exception("Invalid ERC20 token contract event");

            var valueHex = contractEvent.HexData.Substring(PrefixOffset, TopicSizeInHex);

            return new Erc20TransferEventDTO
            {
                From = $"0x{contractEvent.Topics[1][^AddressLengthInHex..]}",
                To = $"0x{contractEvent.Topics[2][^AddressLengthInHex..]}",
                Value = new HexBigInteger(valueHex).Value
            };
        }

        public static Erc20InitiatedEventDTO ParseErc20InitiatedEvent(
            this EtherScanApi.ContractEvent contractEvent)
        {
            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<Erc20InitiatedEventDTO>();

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

            return new Erc20InitiatedEventDTO
            {
                HashedSecret = Hex.FromString(contractEvent.Topics[1], true),
                ERC20Contract = $"0x{contractEvent.Topics[2][^AddressLengthInHex..]}",
                Participant = $"0x{contractEvent.Topics[3][^AddressLengthInHex..]}",
                Initiator = $"0x{initiatorHex[^AddressLengthInHex..]}",
                RefundTimestamp = new HexBigInteger(refundTimeHex).Value,
                Countdown = new HexBigInteger(countdownHex).Value,
                Value = new HexBigInteger(valueHex).Value,
                RedeemFee = new HexBigInteger(redeemFeeHex).Value,
                Active = new HexBigInteger(active).Value != 0
            };
        }

        //public static Erc20AddedEventDTO ParseErc20AddedEvent(
        //    this EtherScanApi.ContractEvent contractEvent)
        //{
        //    var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<Erc20AddedEventDTO>();

        //    if (contractEvent.Topics == null ||
        //        contractEvent.Topics.Count != 2 ||
        //        contractEvent.EventSignatureHash() != eventSignatureHash)
        //        throw new Exception("Invalid contract event");

        //    var initiatorHex = contractEvent.HexData.Substring(PrefixOffset, TopicSizeInHex);
        //    var valueHex = contractEvent.HexData.Substring(PrefixOffset + TopicSizeInHex, TopicSizeInHex);

        //    return new Erc20AddedEventDTO
        //    {
        //        HashedSecret = Hex.FromString(contractEvent.Topics[1], true),
        //        Initiator = $"0x{initiatorHex[^AddressLengthInHex..]}",
        //        Value = new HexBigInteger(valueHex).Value
        //    };
        //}

        //public static Erc20RedeemedEventDTO ParseErc20RedeemedEvent(
        //    this EtherScanApi.ContractEvent contractEvent)
        //{
        //    var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<Erc20RedeemedEventDTO>();

        //    if (contractEvent.Topics == null ||
        //        contractEvent.Topics.Count != 2 ||
        //        contractEvent.EventSignatureHash() != eventSignatureHash)
        //        throw new Exception("Invalid contract event");

        //    return new Erc20RedeemedEventDTO
        //    {
        //        HashedSecret = Hex.FromString(contractEvent.Topics[1], true),
        //        Secret = Hex.FromString(contractEvent.HexData, true)
        //    };
        //}

        //public static Erc20RefundedEventDTO ParseRefundedEvent(
        //    this EtherScanApi.ContractEvent contractEvent)
        //{
        //    var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<Erc20RefundedEventDTO>();

        //    if (contractEvent.Topics == null ||
        //        contractEvent.Topics.Count != 2 ||
        //        contractEvent.EventSignatureHash() != eventSignatureHash)
        //        throw new Exception("Invalid contract event");

        //    return new Erc20RefundedEventDTO
        //    {
        //        HashedSecret = Hex.FromString(contractEvent.Topics[1], true),
        //    };
        //}

        public static EthereumTransaction TransformApprovalEvent(
            this EtherScanApi.ContractEvent contractEvent,
            Erc20Config erc20,
            long lastBlockNumber)
        {
            if (!contractEvent.IsErc20ApprovalEvent())
                return null;

            var approvalEvent = contractEvent.ParseErc20ApprovalEvent();

            var tx = new EthereumTransaction() //todo: make a refactoring
            {
                Currency      = erc20.Name,
                Id            = contractEvent.HexTransactionHash,
                Type          = TransactionType.Output | TransactionType.TokenApprove,
                Status         = TransactionStatus.Confirmed, //todo: check if true in 100% cases
                CreationTime  = contractEvent.HexTimeStamp[PrefixOffset..].FromHexString(),

                From          = approvalEvent.Owner,
                To            = approvalEvent.Spender,
                Amount        = 0,
                ////Nonce 
                GasPrice      = new HexBigInteger(contractEvent.HexGasPrice).Value,
                GasUsed       = new HexBigInteger(contractEvent.HexGasUsed).Value,
                ReceiptStatus = true,
                IsInternal    = false,
                InternalIndex = 0,
                BlockInfo = new BlockInfo
                {
                    Confirmations = 1 + (int)(lastBlockNumber - long.Parse(contractEvent.HexBlockNumber[PrefixOffset..], System.Globalization.NumberStyles.HexNumber)),
                    //    //Confirmations = txReceipt.Status != null
                    //    //? (int)txReceipt.Status.Value
                    //    //: 0,
                    //    //BlockHash = tx.BlockHash,
                    BlockHeight = long.Parse(contractEvent.HexBlockNumber[PrefixOffset..], System.Globalization.NumberStyles.HexNumber),
                    //    //BlockTime = blockTimeStamp,
                    //    //FirstSeen = blockTimeStamp
                }
            };

            return tx;
        }

        public static EthereumTransaction TransformTransferEvent(
            this EtherScanApi.ContractEvent contractEvent,
            string address,
            Erc20Config erc20,
            long lastBlockNumber)
        {
            if (!contractEvent.IsErc20TransferEvent())
                return null;

            var transferEvent = contractEvent.ParseErc20TransferEvent();

            var tx = new EthereumTransaction() //todo: make a refactoring
            {
                Currency = erc20.Name,
                Id = contractEvent.HexTransactionHash,

                Type = transferEvent.From == address
                    ? transferEvent.To == erc20.SwapContractAddress.ToLowerInvariant()   //todo: change to erc20.SwapContractAddress after currencies.json update
                        ? TransactionType.Output | TransactionType.SwapPayment
                        : TransactionType.Output
                    : TransactionType.Input,  //todo: recognize redeem&refund
                Status = TransactionStatus.Confirmed, //todo: check if true in 100% cases
                CreationTime = contractEvent.HexTimeStamp[PrefixOffset..].FromHexString(),

                From = transferEvent.From,
                To = transferEvent.To,
                Amount = transferEvent.Value.ToHexBigInteger(),
                ////Nonce 
                GasPrice = new HexBigInteger(contractEvent.HexGasPrice).Value,
                GasUsed = new HexBigInteger(contractEvent.HexGasUsed).Value,
                ReceiptStatus = true,
                IsInternal = transferEvent.From == erc20.SwapContractAddress.ToLowerInvariant()
                    || transferEvent.To == erc20.SwapContractAddress.ToLowerInvariant(),
                InternalIndex = 0,
                BlockInfo = new BlockInfo
                {
                    Confirmations = 1 + (int)(lastBlockNumber - long.Parse(contractEvent.HexBlockNumber[PrefixOffset..], System.Globalization.NumberStyles.HexNumber)), 
                //    //Confirmations = txReceipt.Status != null
                //    //? (int)txReceipt.Status.Value
                //    //: 0,
                //    //BlockHash = tx.BlockHash,
                    BlockHeight = long.Parse(contractEvent.HexBlockNumber[PrefixOffset..], System.Globalization.NumberStyles.HexNumber),
                //    //BlockTime = blockTimeStamp,
                //    //FirstSeen = blockTimeStamp
                }
            };

            return tx;
        }
    }
}