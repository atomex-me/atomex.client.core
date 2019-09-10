//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Net.Http;
//using System.Threading;
//using System.Threading.Tasks;
//using Atomex.Blockchain.Abstract;
//using Atomex.Blockchain.BitcoinBased;
//using Atomex.Common;
//using NBitcoin;
//using QBitNinja.Client;
//using QBitNinja.Client.Models;
//using Serilog;

//namespace Atomex.Blockchain.Bitcoin
//{
//    public class QBitNinjaApi : IInOutBlockchainApi
//    {
//        public BitcoinBasedCurrency Currency { get; }
//        public int BroadcastAttemptsCount { get; } = 5;

//        public QBitNinjaApi(BitcoinBasedCurrency currency)
//        {
//            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
//        }

//        public async Task<long> GetBalanceAsync(
//            string address,
//            CancellationToken cancellationToken = default(CancellationToken))
//        {
//            var unspentOuts = await GetUnspentOutputsAsync(address, cancellationToken: cancellationToken)
//                .ConfigureAwait(false);

//            return unspentOuts.Sum(o => o.Value);
//        }

//        public Task<ITxPoint> GetInputAsync(
//            string txId,
//            uint inputNo,
//            CancellationToken cancellationToken = default(CancellationToken))
//        {
//            throw new NotImplementedException();
//        }

//        public Task<IEnumerable<ITxPoint>> GetInputsAsync(
//            string txId,
//            CancellationToken cancellationToken = default(CancellationToken))
//        {
//            throw new NotImplementedException();
//        }

//        public async Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(
//            string address,
//            string afterTxId = null,
//            CancellationToken cancellationToken = default(CancellationToken))
//        {
//            var outputs = new List<ITxOutput>();
//            var balance = await GetBalanceModelAsync(address, true, cancellationToken)
//                .ConfigureAwait(false);

//            foreach (var operation in balance.Operations)
//            {
//                outputs.AddRange(operation.ReceivedCoins
//                    .Select(c => new BitcoinBasedTxOutput(coin: c, spentTxPoint: null)));
//            }

//            return outputs;
//        }

//        public async Task<IEnumerable<ITxOutput>> GetReceivedOutputsAsync(
//            string address,
//            string afterTxId = null,
//            CancellationToken cancellationToken = default(CancellationToken))
//        {
//            var outputs = new List<ITxOutput>();
//            var balance = await GetBalanceModelAsync(address, false, cancellationToken)
//                .ConfigureAwait(false);

//            foreach (var operation in balance.Operations)
//            {
//                outputs.AddRange(operation.ReceivedCoins
//                    .Select(c => new BitcoinBasedTxOutput(coin: c, spentTxPoint: null)));
//            }

//            return outputs;
//        }

//        //public async Task<IEnumerable<ITxOutput>> GetSpentOutputsAsync(
//        //    string address,
//        //    string afterTxId = null,
//        //    CancellationToken cancellationToken = default(CancellationToken))
//        //{
//        //    var outputs = new List<ITxOutput>();
//        //    var balance = await GetBalance(address, false, cancellationToken)
//        //        .ConfigureAwait(false);

//        //    foreach (var operation in balance.Operations)
//        //    {
//        //        outputs.AddRange(operation.SpentCoins
//        //            .Select(c => new BitcoinBaseTxOutput(coin: c, isSpent: true)));
//        //    }

//        //    return outputs;
//        //}

//        public Task<IEnumerable<ITxOutput>> GetOutputsAsync(
//            string address,
//            string afterTxId = null,
//            CancellationToken cancellationToken = default(CancellationToken))
//        {
//            throw new NotImplementedException();

//            //Log.Debug("Get outputs for address: {@address}", address);

//            //var outputs = new List<ITxOutput>();
//            //var balance = await GetBalance(address, false, cancellationToken)
//            //    .ConfigureAwait(false);

//            //Log.Debug("Response: {@BalanceModel}", balance);

//            //foreach (var operation in balance.Operations)
//            //{
//            //    var unspent = operation.ReceivedCoins
//            //        .Where(rc => operation.SpentCoins.FirstOrDefault(c => c.Outpoint.Hash == rc.Outpoint.Hash && c.Outpoint.N == rc.Outpoint.N) == null)
//            //        .Select(c =>
//            //            new BitcoinBaseTxOutput(coin: c, isSpent: false));

//            //    var spent = operation.SpentCoins
//            //        .Select(c =>
//            //            new BitcoinBaseTxOutput(coin: c, isSpent: true));

//            //    outputs.AddRange(unspent);
//            //    outputs.AddRange(spent);
//            //}

//            //return outputs;
//        }

//        public async Task<IBlockchainTransaction> GetTransactionAsync(
//            string txId, CancellationToken
//            cancellationToken = default(CancellationToken))
//        {
//            Log.Debug("Get transaction with id: {@txId}", txId);

//            var txResponse = await new QBitNinjaClient(Currency.Network)
//                .GetTransaction(new uint256(txId))
//                .ConfigureAwait(false);

//            Log.Debug("Response: {@TxResponse}", txResponse);

//            return new BitcoinBasedTransaction(
//                currency: Currency,
//                tx: txResponse.Transaction,
//                blockInfo: new BlockInfo
//                {
//                    Fees = txResponse.Fees.Satoshi,
//                    Confirmations = txResponse.Block?.Confirmations ?? 0,
//                    BlockHeight = txResponse.Block?.Height ?? 0,
//                    FirstSeen = txResponse.FirstSeen.UtcDateTime,
//                    BlockTime = txResponse.Block?.BlockTime.UtcDateTime ?? DateTime.MinValue
//                }
//            );
//        }

//        public Task<bool> IsTransactionConfirmed(
//            string txId, CancellationToken
//            cancellationToken = default(CancellationToken))
//        {
//            throw new NotImplementedException();
//        }

//        public Task<ITxPoint> IsTransactionOutputSpent(
//            string txId,
//            uint outputNo,
//            CancellationToken cancellationToken = default(CancellationToken))
//        {
//            throw new NotImplementedException();
//        }

//        public async Task<string> BroadcastAsync(
//            IBlockchainTransaction transaction,
//            CancellationToken cancellationToken = default(CancellationToken))
//        {
//            var tried = 0;

//            var tx = ((BitcoinBasedTransaction) transaction).Tx;
//            var client = new QBitNinjaClient(Currency.Network);
//            BroadcastResponse response;

//            do
//            {
//                tried++;

//                response = await client
//                    .Broadcast(tx)
//                    .ConfigureAwait(false);

//                if (!response.Success)
//                    break;    

//                var checkTx = await client
//                    .GetTransaction(tx.GetHash())
//                    .ConfigureAwait(false);

//                if (checkTx != null)
//                    return checkTx.TransactionId.ToString();

//                await Task.Delay(1000, cancellationToken)
//                    .ConfigureAwait(false);

//                if (cancellationToken.IsCancellationRequested)
//                    cancellationToken.ThrowIfCancellationRequested();

//            } while (tried <= BroadcastAttemptsCount);

//            throw new Exception($"Invalid response code: {response.Error.ErrorCode}");
//        }

//        public Task<ConfidenceInformation> GetConfidenceAsync(string txId, CancellationToken cancellationToken = default(CancellationToken))
//        {
//            throw new NotImplementedException();
//        }

//        private async Task<BalanceModel> GetBalanceModelAsync(
//            string address,
//            bool unspentOnly = false,
//            CancellationToken cancellationToken = default(CancellationToken))
//        {
//            //return await new QBitNinjaClient(Network)
//            //    .GetBalance(address, unspentOnly: true);

//            string baseAddress;

//            if (Currency.Network == Network.Main)
//                baseAddress = "http://api.qbit.ninja/";
//            else if (Currency.Network == Network.TestNet)
//                baseAddress = "http://tapi.qbit.ninja/";
//            else
//                throw new NotSupportedException("Network not supported by QBitNinjaApi");

//            return await HttpHelper.GetAsync(
//                    baseUri: baseAddress,
//                    requestUri: $"balances/{address}?unspentonly={unspentOnly}",
//                    responseHandler: responseContent =>
//                    {
//                        return Serializer.ToObject<BalanceModel>(responseContent);
//                    },
//                    cancellationToken: cancellationToken)
//                .ConfigureAwait(false);
//        }

//        public Task<IEnumerable<IBlockchainTransaction>> GetAllTransactionsByIdAsync(
//            string txId,
//            CancellationToken cancellationToken = default(CancellationToken))
//        {
//            throw new NotImplementedException();
//        }
//    }
//}