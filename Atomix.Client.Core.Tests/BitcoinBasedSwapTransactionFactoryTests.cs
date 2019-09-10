using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.Swaps.BitcoinBased;
using Moq;
using NBitcoin;
using NBitcoin.Altcoins;
using Xunit;

namespace Atomix.Client.Core.Tests
{
    public class BitcoinBasedSwapTransactionFactoryTests
    {
        private static WalletAddress GetWallet(BitcoinBasedCurrency currency, PubKey pubKey)
        {
            return new WalletAddress
            {
                Address = pubKey.GetAddress(ScriptPubKeyType.Legacy, currency.Network).ToString(),
                Currency = currency,
                PublicKey = Convert.ToBase64String(pubKey.ToBytes())
            };
        }

        private static IEnumerable<ITxOutput> GetTestOutputs(PubKey pubKey, NBitcoin.Network network)
        {
            var tx = Transaction.Create(network);
            tx.Outputs.Add(new TxOut(new Money(10000L), pubKey.Hash));
            tx.Outputs.Add(new TxOut(new Money(20000L), pubKey.Hash));
            tx.Outputs.Add(new TxOut(new Money(30000L), pubKey.Hash));

            return tx.Outputs
                .AsCoins()
                .Select(c => new BitcoinBasedTxOutput(c));
        }

        [Fact]
        public async Task<IBlockchainTransaction> CreateSwapPaymentTxTest()
        {
            var bitcoinApi = new Mock<IInOutBlockchainApi>();
            bitcoinApi.Setup(a => a.GetUnspentOutputsAsync(It.IsAny<string>(), null, new CancellationToken()))
                .Returns(Task.FromResult(GetTestOutputs(Common.Alice.PubKey, NBitcoin.Network.TestNet)));

            var litecoinApi = new Mock<IInOutBlockchainApi>();
            litecoinApi.Setup(a => a.GetUnspentOutputsAsync(It.IsAny<string>(), null, new CancellationToken()))
                .Returns(Task.FromResult(GetTestOutputs(Common.Bob.PubKey, AltNetworkSets.Litecoin.Testnet)));

            var tempCurrencies = new Currencies(Common.CurrenciesConfiguration.GetSection(Atomix.Core.Network.TestNet.ToString()));

            var bitcoin = tempCurrencies.Get<Bitcoin>();
            bitcoin.BlockchainApi = bitcoinApi.Object;

            var litecoin = tempCurrencies.Get<Litecoin>();
            litecoin.BlockchainApi = litecoinApi.Object;

            var aliceBtcWallet = GetWallet(bitcoin, Common.Alice.PubKey);
            //var aliceLtcWallet = GetWallet(litecoin, Common.Alice.PubKey);

            var bobBtcWallet = GetWallet(bitcoin, Common.Bob.PubKey);
            //var bobLtcWallet = GetWallet(litecoin, Common.Bob.PubKey);

            const decimal lastPrice = 0.000001m;
            const decimal lastQty = 10m;

            var swap = new ClientSwap
            {
                Symbol = new Symbol { Base = litecoin, Quote = bitcoin },
                Side = Side.Buy,
                Price = lastPrice,
                Qty = lastQty
            };

            var tx = await new BitcoinBasedSwapTransactionFactory()
                .CreateSwapPaymentTxAsync(
                    currency: bitcoin,
                    swap: swap,
                    fromWallets: new []{ aliceBtcWallet.Address },
                    refundAddress: aliceBtcWallet.Address,
                    toAddress: bobBtcWallet.Address,
                    lockTime: DateTimeOffset.UtcNow.AddHours(1),
                    secretHash: Common.SecretHash,
                    secretSize: Common.Secret.Length,
                    outputsSource: new BlockchainTxOutputSource())
                .ConfigureAwait(false);

            Assert.NotNull(tx);

            return tx;
        }


        //[Fact]
        //public void SignSwapPaymentTxTest()
        //{
        //    throw new NotImplementedException();
        //}
    }
}