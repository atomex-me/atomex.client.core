using System;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Nethereum.Hex.HexTypes;

namespace Atomex.Blockchain.Ethereum.ERC20
{
    public static class ERC20EtherScanExtensions
    {
        private const int AddressLengthInHex = 40;
        private const int TopicSizeInHex = 64;

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

        public static ERC20ApprovalEventDTO ParseERC20ApprorovalEvent(
            this EtherScanApi.ContractEvent contractEvent)
        {
            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<ERC20ApprovalEventDTO>();

            if (contractEvent.Topics == null ||
                contractEvent.Topics.Count != 3 ||
                contractEvent.EventSignatureHash() != eventSignatureHash)
                throw new Exception("Invalid ERC20 token contract event");

            const int prefixOffset = 2;
            var valueHex = contractEvent.HexData.Substring(prefixOffset, TopicSizeInHex);

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

            const int prefixOffset = 2;
            var valueHex = contractEvent.HexData.Substring(prefixOffset, TopicSizeInHex);

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

            const int prefixOffset = 2;
            var initiatorHex = contractEvent.HexData.Substring(prefixOffset, TopicSizeInHex);
            var refundTimeHex = contractEvent.HexData.Substring(prefixOffset + TopicSizeInHex, TopicSizeInHex);
            var countdownHex = contractEvent.HexData.Substring(prefixOffset + 2 * TopicSizeInHex, TopicSizeInHex);
            var valueHex = contractEvent.HexData.Substring(prefixOffset + 3 * TopicSizeInHex, TopicSizeInHex);
            var redeemFeeHex = contractEvent.HexData.Substring(prefixOffset + 4 * TopicSizeInHex, TopicSizeInHex);
            var active = contractEvent.HexData.Substring(prefixOffset + 5 * TopicSizeInHex, TopicSizeInHex);

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

            const int prefixOffset = 2;
            var initiatorHex = contractEvent.HexData.Substring(prefixOffset, TopicSizeInHex);
            var valueHex = contractEvent.HexData.Substring(prefixOffset + TopicSizeInHex, TopicSizeInHex);

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

        public static EthereumTransaction TransformTransferEvent(
            this EtherScanApi.ContractEvent contractEvent,
            EthereumTokens.ERC20 erc20,
            long lastBlockNumber)
        {
            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<ERC20TransferEventDTO>();

            if (contractEvent.Topics == null ||
                contractEvent.Topics.Count != 3 ||
                contractEvent.EventSignatureHash() != eventSignatureHash)
                throw new Exception("Invalid ERC20 token contract event");

            const int prefixOffset = 2;
            var valueHex = contractEvent.HexData.Substring(prefixOffset, TopicSizeInHex);

            var tx = new EthereumTransaction() //todo: make a refactoring
            {
                Currency = erc20,
                Id = contractEvent.HexTransactionHash,
                Type = BlockchainTransactionType.Unknown,
                State = BlockchainTransactionState.Confirmed, //todo: check if true in 100% cases
                CreationTime = contractEvent.HexTimeStamp.Substring(prefixOffset).FromHexString(),

                From =
                    $"0x{contractEvent.Topics[1].Substring(contractEvent.Topics[1].Length - AddressLengthInHex)}",
                To =
                    $"0x{contractEvent.Topics[2].Substring(contractEvent.Topics[2].Length - AddressLengthInHex)}",
                Amount = new HexBigInteger(valueHex).Value,
                ////Nonce 
                GasPrice = new HexBigInteger(contractEvent.HexGasPrice).Value,
                ////GasLimit
                GasLimit = new HexBigInteger(contractEvent.HexGasUsed).Value,
                ReceiptStatus = true,
                IsInternal = true,
                InternalIndex = 0,
                BlockInfo = new BlockInfo
                {
                    Confirmations = 1 + (int)(lastBlockNumber - long.Parse(contractEvent.HexBlockNumber.Substring(prefixOffset), System.Globalization.NumberStyles.HexNumber)), 
                //    //Confirmations = txReceipt.Status != null
                //    //? (int)txReceipt.Status.Value
                //    //: 0,
                //    //BlockHash = tx.BlockHash,
                    BlockHeight = long.Parse(contractEvent.HexBlockNumber.Substring(prefixOffset), System.Globalization.NumberStyles.HexNumber),
                //    //BlockTime = blockTimeStamp,
                //    //FirstSeen = blockTimeStamp
                }
            };

            return tx;
        }
    }
}