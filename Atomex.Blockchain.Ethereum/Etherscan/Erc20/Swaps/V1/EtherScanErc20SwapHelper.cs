using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Ethereum.Common;
using Atomex.Blockchain.Ethereum.Dto.Erc20.Swaps.V1;
using Atomex.Blockchain.Ethereum.Messages.Erc20.Swaps.V1;
using Atomex.Common;

namespace Atomex.Blockchain.Ethereum.Etherscan.Erc20.Swaps.V1
{
    public class EtherScanErc20SwapHelper
    {
        public static async Task<(IEnumerable<EthereumTransaction> txs, Error error)> FindLocksAsync(
            EtherScanApi api,
            string secretHash,
            string contractAddress,
            ulong contractBlock,
            string address,
            ulong timeStamp,
            ulong lockTime,
            string tokenContract,
            CancellationToken cancellationToken = default)
        {
            var (events, eventsError) = await api
                .GetContractEventsAsync(
                    address: contractAddress,
                    fromBlock: contractBlock,
                    toBlock: ulong.MaxValue,
                    topic0: EventSignatureExtractor.GetSignatureHash<Erc20InitiatedEventDto>(),
                    topic1: "0x" + secretHash,
                    topic2: "0x000000000000000000000000" + tokenContract[2..],
                    topic3: "0x000000000000000000000000" + address[2..],
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (eventsError != null)
                return (txs: null, eventsError);

            if (events == null || !events.Any())
                return (txs: null, error: null);

            var (tx, txError) = await api
                .GetTransactionAsync(
                    txId: events.First().HexTransactionHash,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txError != null)
                return (txs: null, error: txError);

            var ethTx = tx as EthereumTransaction;

            Erc20InitiateMessage.TryParse(ethTx.Data, out var initiateMessage);

            if (initiateMessage.RefundTimeStamp < (long)(timeStamp + lockTime))
                return (txs: null, error: null);

            return (txs: new EthereumTransaction[] { ethTx }, error: null);
        }

        public static async Task<(IEnumerable<EthereumTransaction> txs, Error error)> FindAdditionalLocksAsync(
            EtherScanApi api,
            string secretHash,
            string contractAddress,
            ulong contractBlock,
            CancellationToken cancellationToken = default)
        {
            var (events, eventsError) = await api
                .GetContractEventsAsync(
                    address: contractAddress,
                    fromBlock: contractBlock,
                    toBlock: ulong.MaxValue,
                    topic0: EventSignatureExtractor.GetSignatureHash<Erc20AddedEventDto>(),
                    topic1: "0x" + secretHash,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (eventsError != null)
                return (txs: null, eventsError);

            if (events == null || !events.Any())
                return (txs: null, error: null);

            var txs = await Task.WhenAll(events.Select(async e =>
            {
                var (tx, _) = await api
                    .GetTransactionAsync(
                        txId: e.HexTransactionHash,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                return tx as EthereumTransaction;
            }));

            return (txs, error: null);
        }

        public static async Task<(IEnumerable<EthereumTransaction> txs, Error error)> FindRedeemsAsync(
            EtherScanApi api,
            string secretHash,
            string contractAddress,
            ulong contractBlock,
            CancellationToken cancellationToken = default)
        {
            var (events, eventsError) = await api
                .GetContractEventsAsync(
                    address: contractAddress,
                    fromBlock: contractBlock,
                    toBlock: ulong.MaxValue,
                    topic0: EventSignatureExtractor.GetSignatureHash<Erc20RedeemedEventDto>(),
                    topic1: "0x" + secretHash,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (eventsError != null)
                return (txs: null, eventsError);

            if (events == null || !events.Any())
                return (txs: null, error: null);

            var (tx, txError) = await api
                .GetTransactionAsync(
                    txId: events.First().HexTransactionHash,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txError != null)
                return (txs: null, error: txError);

            var ethTx = tx as EthereumTransaction;

            return (txs: new EthereumTransaction[] { ethTx }, error: null);
        }

        public static async Task<(IEnumerable<EthereumTransaction> txs, Error error)> FindRefundsAsync(
            EtherScanApi api,
            string secretHash,
            string contractAddress,
            ulong contractBlock,
            CancellationToken cancellationToken = default)
        {
            var (events, eventsError) = await api
                .GetContractEventsAsync(
                    address: contractAddress,
                    fromBlock: contractBlock,
                    toBlock: ulong.MaxValue,
                    topic0: EventSignatureExtractor.GetSignatureHash<Erc20RefundedEventDTO>(),
                    topic1: "0x" + secretHash,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (eventsError != null)
                return (txs: null, eventsError);

            if (events == null || !events.Any())
                return (txs: null, error: null);

            var (tx, txError) = await api
                .GetTransactionAsync(
                    txId: events.First().HexTransactionHash,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txError != null)
                return (txs: null, error: txError);

            var ethTx = tx as EthereumTransaction;

            return (txs: new EthereumTransaction[] { ethTx }, error: null);
        }
    }
}