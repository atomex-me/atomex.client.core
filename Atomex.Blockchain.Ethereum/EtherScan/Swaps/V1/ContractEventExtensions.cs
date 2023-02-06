using System;

using Nethereum.Hex.HexTypes;

using Atomex.Blockchain.Ethereum.Dto.Swaps.V1;
using Atomex.Common;

namespace Atomex.Blockchain.Ethereum.EtherScan
{
    public static class ContractEventExtensions
    {
        private const int AddressLengthInHex = 40;
        private const int TopicSizeInHex = 64;

        public static InitiatedEventDTO ParseInitiatedEvent(this ContractEvent contractEvent)
        {
            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<InitiatedEventDTO>();

            if (contractEvent.Topics == null ||
                contractEvent.Topics.Count != 3 ||
                contractEvent.EventSignatureHash() != eventSignatureHash)
                throw new Exception("Invalid contract event");

            const int prefixOffset = 2;
            var initiatorHex  = contractEvent.HexData.Substring(prefixOffset, TopicSizeInHex);
            var refundTimeHex = contractEvent.HexData.Substring(prefixOffset + TopicSizeInHex, TopicSizeInHex);
            var valueHex      = contractEvent.HexData.Substring(prefixOffset + 2 * TopicSizeInHex, TopicSizeInHex);
            var redeemFeeHex  = contractEvent.HexData.Substring(prefixOffset + 3 * TopicSizeInHex, TopicSizeInHex);

            return new InitiatedEventDTO
            {
                HashedSecret    = Hex.FromString(contractEvent.Topics[1], true),
                Participant     = $"0x{contractEvent.Topics[2][^AddressLengthInHex..]}",
                Initiator       = $"0x{initiatorHex[^AddressLengthInHex..]}",
                RefundTimestamp = new HexBigInteger(refundTimeHex).Value,
                Value           = new HexBigInteger(valueHex).Value,
                RedeemFee       = new HexBigInteger(redeemFeeHex).Value
            };
        }

        public static AddedEventDTO ParseAddedEvent(this ContractEvent contractEvent)
        {
            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<AddedEventDTO>();

            if (contractEvent.Topics == null ||
                contractEvent.Topics.Count != 2 ||
                contractEvent.EventSignatureHash() != eventSignatureHash)
                throw new Exception("Invalid contract event");

            const int prefixOffset = 2;
            var initiatorHex = contractEvent.HexData.Substring(prefixOffset, TopicSizeInHex);
            var valueHex     = contractEvent.HexData.Substring(prefixOffset + TopicSizeInHex, TopicSizeInHex);

            return new AddedEventDTO
            {
                HashedSecret = Hex.FromString(contractEvent.Topics[1], true),
                Initiator    = $"0x{initiatorHex[^AddressLengthInHex..]}",
                Value        = new HexBigInteger(valueHex).Value
            };
        }

        public static RedeemedEventDTO ParseRedeemedEvent(this ContractEvent contractEvent)
        {
            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<RedeemedEventDTO>();

            if (contractEvent.Topics == null ||
                contractEvent.Topics.Count != 2 ||
                contractEvent.EventSignatureHash() != eventSignatureHash)
                throw new Exception("Invalid contract event");

            return new RedeemedEventDTO
            {
                HashedSecret = Hex.FromString(contractEvent.Topics[1], true),
                Secret       = Hex.FromString(contractEvent.HexData, true)
            };
        }
    }
}