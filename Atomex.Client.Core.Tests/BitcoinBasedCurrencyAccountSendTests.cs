using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Atomex.Wallet;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;
using Moq;
using NBitcoin;
using Xunit;
using Network = Atomex.Core.Network;

namespace Atomex.Client.Core.Tests
{
    public class BitcoinBasedCurrencyAccountSendTests
    {
        private IEnumerable<ITxOutput> GetOutputs(string address, NBitcoin.Network network, params long[] values)
        {
            var bitcoinAddress = BitcoinAddress.Create(address, network);

            var tx = Transaction.Create(NBitcoin.Network.TestNet);

            foreach (var value in values)
                tx.Outputs.Add(new TxOut(new Money(value), bitcoinAddress));

            return tx.Outputs
                .AsCoins()
                .Select(c => new BitcoinBasedTxOutput(c));
        }

        private T GetCurrency<T>(IBlockchainApi api = null) where T : Currency
        {
            var currency = Common.CurrenciesTestNet.Get<T>();

            if (api != null)
                currency.BlockchainApi = api;

            return currency;
        }

        private Error Send(
            decimal available,
            decimal amount,
            decimal fee,
            DustUsagePolicy dustUsagePolicy,
            Action<Mock<IInOutBlockchainApi>> apiSetup = null,
            Action<Mock<IAccountDataRepository>, WalletAddress> repositorySetup = null)
        {
            var apiMock = new Mock<IInOutBlockchainApi>();
            apiSetup?.Invoke(apiMock);

            var currency = GetCurrency<Bitcoin>(apiMock.Object);
            var wallet = new HdWallet(Network.TestNet);
            var fromAddress = wallet.GetAddress(currency, 0, 0);
            var fromOutputs = GetOutputs(fromAddress.Address, NBitcoin.Network.TestNet, currency.CoinToSatoshi(available)).ToList();

            var repositoryMock = new Mock<IAccountDataRepository>();
            repositorySetup?.Invoke(repositoryMock, fromAddress);

            var account = new BitcoinBasedCurrencyAccount(
                currency: currency,
                wallet: wallet,
                dataRepository: repositoryMock.Object);

            return account.SendAsync(
                    outputs: fromOutputs,
                    to: currency.TestAddress(),
                    amount: amount,
                    fee: fee,
                    dustUsagePolicy: dustUsagePolicy)
                .WaitForResult();
        }

        [Fact]
        public void SendTest()
        {
            const decimal available = 0.00100000m;
            const decimal amount = 0.00090000m;
            const decimal fee = 0.00010000m;

            var error = Send(
                available: available,
                amount: amount,
                fee: fee,
                dustUsagePolicy: DustUsagePolicy.Warning,
                apiSetup: apiMock =>
                {
                    apiMock.Setup(a => a.BroadcastAsync(It.IsAny<IBlockchainTransaction>(), CancellationToken.None))
                        .Returns(Task.FromResult(new Result<string>("<txid>")));
                },
                repositorySetup: (repositoryMock, fromAddress) =>
                {
                    repositoryMock.Setup(r => r.GetWalletAddressAsync(It.IsAny<Currency>(), fromAddress.Address))
                        .Returns(Task.FromResult(fromAddress));
                });

            Assert.Null(error);
        }

        [Fact]
        public void SendDustAmountFailTest()
        {
            const decimal available = 0.00100000m;
            const decimal amount = 0.00000090m; // dust amount
            const decimal fee = 0.00010000m;
            const DustUsagePolicy dustUsagePolicy = DustUsagePolicy.Warning;

            var error = Send(
                available: available,
                amount: amount,
                fee: fee,
                dustUsagePolicy: dustUsagePolicy);

            Assert.NotNull(error);
            Assert.Equal(Errors.InsufficientAmount, error.Code);
        }

        [Fact]
        public void SendInsufficientFundsFailTest()
        {
            const decimal available = 0.00100000m;
            const decimal amount = 0.00090001m; // amount + fee = 100001 > 100000
            const decimal fee = 0.00010000m;
            const DustUsagePolicy dustUsagePolicy = DustUsagePolicy.Warning;

            var error = Send(
                available: available,
                amount: amount,
                fee: fee,
                dustUsagePolicy: dustUsagePolicy);

            Assert.NotNull(error);
            Assert.Equal(Errors.InsufficientFunds, error.Code);
        }

        [Fact]
        public void SendDustChangeFailTest()
        {
            const decimal available = 0.00100000m;
            const decimal amount = 0.00090000m; // change {500} is dust 
            const decimal fee = 0.00009500m;
            const DustUsagePolicy dustUsagePolicy = DustUsagePolicy.Warning;

            var error = Send(
                available: available,
                amount: amount,
                fee: fee,
                dustUsagePolicy: dustUsagePolicy);

            Assert.NotNull(error);
            Assert.Equal(Errors.InsufficientAmount, error.Code);
        }

        [Fact]
        public void SendDustAsAmountTest()
        {
            const decimal available = 0.00100000m;
            const decimal amount = 0.00090000m; // change {500} is dust and will be add to amount {90000 + 500}
            const decimal fee = 0.00009500m;
            const DustUsagePolicy dustUsagePolicy = DustUsagePolicy.AddToDestination;

            var broadcastCallback = new Action<IBlockchainTransaction, CancellationToken>((tx, token) =>
            {
                var btcBasedTx = (IBitcoinBasedTransaction) tx;
                Assert.NotNull(btcBasedTx.Outputs.FirstOrDefault(o => o.Value == 90500L));
            });

            var error = Send(
                available: available,
                amount: amount,
                fee: fee,
                dustUsagePolicy: dustUsagePolicy,
                apiSetup: apiMock =>
                {
                    apiMock.Setup(a => a.BroadcastAsync(It.IsAny<IBlockchainTransaction>(), CancellationToken.None))
                        .Callback(broadcastCallback)
                        .Returns(Task.FromResult(new Result<string>("<txid>")));
                },
                repositorySetup: (repositoryMock, fromAddress) =>
                {
                    repositoryMock.Setup(r => r.GetWalletAddressAsync(It.IsAny<Currency>(), fromAddress.Address))
                        .Returns(Task.FromResult(fromAddress));
                });

            Assert.Null(error);
        }

        [Fact]
        public void SendDustAsFeeTest()
        {
            const decimal available = 0.00100000m;
            const decimal amount = 0.00090000m; 
            const decimal fee = 0.00009500m; // change {500} is dust and will be add to fee {9500 + 500}
            const DustUsagePolicy dustUsagePolicy = DustUsagePolicy.AddToFee;

            var broadcastCallback = new Action<IBlockchainTransaction, CancellationToken>((tx, token) =>
            {
                var btcBasedTx = (IBitcoinBasedTransaction)tx;
                Assert.True(btcBasedTx.Fees == 10000L);
            });

            var error = Send(
                available: available,
                amount: amount,
                fee: fee,
                dustUsagePolicy: dustUsagePolicy,
                apiSetup: apiMock =>
                {
                    apiMock.Setup(a => a.BroadcastAsync(It.IsAny<IBlockchainTransaction>(), CancellationToken.None))
                        .Callback(broadcastCallback)
                        .Returns(Task.FromResult(new Result<string>("<txid>")));
                },
                repositorySetup: (repositoryMock, fromAddress) =>
                {
                    repositoryMock.Setup(r => r.GetWalletAddressAsync(It.IsAny<Currency>(), fromAddress.Address))
                        .Returns(Task.FromResult(fromAddress));
                });

            Assert.Null(error);
        }
    }
}