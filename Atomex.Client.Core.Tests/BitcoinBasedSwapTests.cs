using System.Collections.Generic;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Core;
using Atomex.Core.Entities;
using NBitcoin;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class BitcoinBasedSwapTests
    {
        public Order InitiatorOrder => new Order
        {
            Symbol = Common.LtcBtcTestNet,
            Price = 0.014861m,
            Qty = 1,
            LastPrice = 0.014861m,
            LastQty = 1,
            Side = Side.Buy,
            Status = OrderStatus.Filled,
            EndOfTransaction = true,
            FromWallets = new List<WalletAddress>
            {
                new WalletAddress
                {
                    Address = "initiator BTC payment wallet",
                    Currency = Common.BtcTestNet
                }
            }
        };

        public IEnumerable<ITxOutput> InitiatorOutputs => new List<ITxOutput>
        {
            new BitcoinBasedTxOutput(new Coin
            {
                Amount = Money.Satoshis(10000000000ul),
                Outpoint = new OutPoint(new uint256(), 0)
            })
        };

        [Fact]
        public void InitiateSwapTest()
        {
            // setup environment
            //var account = new Mock<IAccount>();

            //account.Setup(a => a.GetUnspentOutputsAsync(Currencies.Btc, It.IsAny<string>(), true))
            //    .Returns(Task.FromResult(InitiatorOutputs));

            //var swapClient = new Mock<ISwapClient>();

            //var taskPerformer = new Mock<IBackgroundTaskPerformer>();

            //var transactionFactory = new Mock<IBitcoinBasedSwapTransactionFactory>();

            //transactionFactory.Setup(f => f.CreateSwapPaymentTxAsync(
            //        Currencies.Btc,
            //        It.IsAny<Order>(),
            //        It.IsAny<SwapRequisites>(),
            //        It.IsAny<byte[]>(),
            //        It.IsAny<ITxOutputSource>()))
            //    .Returns(Task.FromResult<IBitcoinBasedTransaction>(new BitcoinBasedTransaction(Currencies.Btc,
            //        Transaction.Create(Currencies.Btc.Network))));

            //var swapState = new SwapState(InitiatorOrder, PartyRequisites);
            ////swapState.Updated += (sender, args) => { };

            //// test
            //var swap = new BitcoinBasedSwap(
            //    currency: Currencies.Btc,
            //    swapState: swapState,
            //    account: account.Object,
            //    swapClient: swapClient.Object,
            //    taskPerformer: taskPerformer.Object,
            //    transactionFactory: transactionFactory.Object);

            //await swap.InitiateSwapAsync();

            
        }
    }
}