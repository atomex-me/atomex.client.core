using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.BitcoinBased;
using Moq;
using NBitcoin;
using NBitcoin.Altcoins;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class BitcoinBasedSwapTransactionFactoryTests
    {
        private static Result<IEnumerable<ITxOutput>> GetTestOutputs(PubKey pubKey, NBitcoin.Network network)
        {
            var tx = Transaction.Create(network);
            tx.Outputs.Add(new TxOut(new Money(100000L), pubKey.Hash));
            tx.Outputs.Add(new TxOut(new Money(200000L), pubKey.Hash));
            tx.Outputs.Add(new TxOut(new Money(300000L), pubKey.Hash));

            var outputs = tx.Outputs
                .AsCoins()
                .Select(c => new BitcoinBasedTxOutput(c));

            return new Result<IEnumerable<ITxOutput>>(outputs);
        }

        [Fact]
        public async Task<IBlockchainTransaction_OLD> CreateSwapPaymentTxTest()
        {
            var bitcoinApi = new Mock<IInOutBlockchainApi_OLD>();
            bitcoinApi.Setup(a => a.GetUnspentOutputsAsync(It.IsAny<string>(), null, new CancellationToken()))
                .Returns(Task.FromResult(GetTestOutputs(Common.Alice.PubKey, NBitcoin.Network.TestNet)));

            var litecoinApi = new Mock<IInOutBlockchainApi_OLD>();
            litecoinApi.Setup(a => a.GetUnspentOutputsAsync(It.IsAny<string>(), null, new CancellationToken()))
                .Returns(Task.FromResult(GetTestOutputs(Common.Bob.PubKey, AltNetworkSets.Litecoin.Testnet)));

            var tempCurrencies = new CurrenciesProvider(Common.CurrenciesConfigurationString)
                .GetCurrencies(Atomex.Core.Network.TestNet);

            var bitcoin = tempCurrencies.Get<BitcoinConfig>("BTC");
            bitcoin.BlockchainApi = bitcoinApi.Object;

            var litecoin = tempCurrencies.Get<LitecoinConfig>("LTC");
            litecoin.BlockchainApi = litecoinApi.Object;

            var aliceBtcAddress = Common.Alice.PubKey
                .GetAddress(ScriptPubKeyType.Legacy, bitcoin.Network)
                .ToString();

            var bobBtcAddress = Common.Bob.PubKey
                .GetAddress(ScriptPubKeyType.Legacy, bitcoin.Network)
                .ToString();

            const decimal lastPrice = 0.000001m;
            const decimal lastQty = 10m;

            var swap = new Swap
            {
                Symbol = "LTC/BTC",
                Side = Side.Buy,
                Price = lastPrice,
                Qty = lastQty
            };

            var amountInSatoshi = bitcoin.CoinToSatoshi(AmountHelper.QtyToSellAmount(swap.Side, swap.Qty, swap.Price, bitcoin.DigitsMultiplier));

            var outputs = (await new BlockchainTxOutputSource(bitcoin)
                .GetAvailableOutputsAsync(new[] { aliceBtcAddress }))
                .Cast<BitcoinBasedTxOutput>();

            var tx = await new BitcoinBasedSwapTransactionFactory()
                .CreateSwapPaymentTxAsync(
                    fromOutputs: outputs,
                    amount: amountInSatoshi,
                    refundAddress: aliceBtcAddress,
                    toAddress: bobBtcAddress,
                    lockTime: DateTimeOffset.UtcNow.AddHours(1),
                    secretHash: Common.SecretHash,
                    secretSize: Common.Secret.Length,
                    currencyConfig: bitcoin)
                .ConfigureAwait(false);

            Assert.NotNull(tx);
            //Assert.NotNull(redeemScript);

            return tx;
        }
    }
}