using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Common.Abstract;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.Swaps;
using Atomix.Swaps.Abstract;
using Atomix.Swaps.BitcoinBased;
using Atomix.Wallet.Abstract;
using Moq;
using NBitcoin;
using Xunit;

namespace Atomix.Client.Core.Tests
{
    public class BitcoinBasedSwapTests
    {
        public Order InitiatorOrder => new Order()
        {
            Symbol = Symbols.LtcBtc,
            Price = 0.014861m,
            Qty = 1,
            LastPrice = 0.014861m,
            LastQty = 1,
            Side = Side.Buy,
            Status = OrderStatus.Filled,
            EndOfTransaction = true,
            SwapInitiative = true,
            RefundWallet = new WalletAddress()
            {
                Address = "<initiator BTC refund wallet>",
                Currency = Currencies.Btc
            },
            ToWallet = new WalletAddress()
            {
                Address = "<initiator LTC target wallet>",
                Currency = Currencies.Ltc
            },
            FromWallets = new List<WalletAddress>()
            {
                new WalletAddress()
                {
                    Address = "initiator BTC payment wallet",
                    Currency = Currencies.Btc
                }
            }
        };

        public IEnumerable<ITxOutput> InitiatorOutputs => new List<ITxOutput>()
        {
            new BitcoinBasedTxOutput(new Coin()
            {
                Amount = Money.Satoshis(10000000000ul),
                Outpoint = new OutPoint(new uint256(), 0)
            })
        };

        public SwapRequisites PartyRequisites => new SwapRequisites()
        {
            ToWallet = new WalletAddress()
            {
                Address = "<party BTC target wallet>",
                Currency = Currencies.Btc
            },
            RefundWallet = new WalletAddress()
            {
                Address = "<party LTC refund wallet>",
                Currency = Currencies.Ltc
            }
        };

        [Fact]
        public async void InitiateSwapTest()
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