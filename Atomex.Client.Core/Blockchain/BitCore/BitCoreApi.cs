using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using Newtonsoft.Json;

namespace Atomex.Blockchain.BitCore
{
    public class BitCoreApi : IInOutBlockchainApi
    {
        internal class AddressBalance
        {
            [JsonProperty(PropertyName = "confirmed")]
            public long Confirmed { get; set; }

            [JsonProperty(PropertyName = "unconfirmed")]
            public long Unconfirmed { get; set; }

            [JsonProperty(PropertyName = "balance")]
            public long Balance { get; set; }
        }

        internal class Tx
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }

            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }

            [JsonProperty(PropertyName = "chain")]
            public string Chain { get; set; }

            [JsonProperty(PropertyName = "blockHeight")]
            public int? BlockHeight { get; set; }

            [JsonProperty(PropertyName = "blockHash")]
            public string BlockHash { get; set; }

            [JsonProperty(PropertyName = "blockTime")]
            public DateTime BlockTime { get; set; }

            [JsonProperty(PropertyName = "size")]
            public int Size { get; set; }

            [JsonProperty(PropertyName = "fee")]
            public long Fee { get; set; }

            [JsonProperty(PropertyName = "value")]
            public long Value { get; set; }

            [JsonProperty(PropertyName = "confirmations")]
            public int Confirmations { get; set; }
        }


        public const string BitCoreBaseUri = "https://api.bitcore.io/";

        private const int MinDelayBetweenRequestMs = 1000;

        private string BaseUri { get; }

        private static readonly RequestLimitControl RequestLimitControl
            = new RequestLimitControl(MinDelayBetweenRequestMs);

        public BitcoinBasedCurrency Currency { get; }

        public BitCoreApi(BitcoinBasedCurrency currency, IConfiguration configuration)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            BaseUri = configuration["BlockchainApiBaseUri"];
        }

        public BitCoreApi(BitcoinBasedCurrency currency, string baseUri)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            BaseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
        }

        public async Task<Result<decimal>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api/{Currency.Name}/{Currency.Network.ToString().ToLower()}/address/{address}/balance/";

            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) => new Result<decimal>(
                        JsonConvert.DeserializeObject<AddressBalance>(content).Balance),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<IBlockchainTransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
            //var txHex = Rpc.GetRawTransaction(txId, 0).Hex;

            //var requestUri = $"api/{Currency.Name}/{Currency.Network.ToString().ToLower()}/tx/{txId}";

            //await RequestLimitControl
            //    .Wait(cancellationToken)
            //    .ConfigureAwait(false);

            //return await HttpHelper.GetAsyncResult(
            //        baseUri: BaseUri,
            //        requestUri: requestUri,
            //        responseHandler: (response, content) =>
            //        {
            //            var tx = JsonConvert.DeserializeObject<Tx>(content);

            //            return new Result<IBlockchainTransaction>(new BitcoinBasedTransaction(
            //                currency: Currency,
            //                tx: Transaction.Parse(txHex, Currency.Network),
            //                blockInfo: new BlockInfo
            //                {
            //                    Confirmations = tx.Confirmations,
            //                    BlockHeight = tx.BlockHeight.GetValueOrDefault(0),
            //                    FirstSeen = tx.BlockTime,
            //                    BlockTime = tx.BlockTime
            //                },
            //                fees: tx.Fee
            //            ));
            //        },
            //        cancellationToken: cancellationToken)
            //    .ConfigureAwait(false);
        }

        public Task<Result<string>> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task<Result<ITxPoint>> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task<Result<IEnumerable<ITxOutput>>> GetUnspentOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task<Result<IEnumerable<ITxOutput>>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task<Result<ITxPoint>> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }
    }
}