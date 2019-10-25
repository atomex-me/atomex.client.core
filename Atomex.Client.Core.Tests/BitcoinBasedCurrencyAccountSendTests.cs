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

        private Error Send(
            BitcoinBasedCurrency currency,
            decimal available,
            decimal amount,
            decimal fee,
            DustUsagePolicy dustUsagePolicy,
            Action<Mock<IInOutBlockchainApi>> apiSetup = null,
            Action<Mock<IAccountDataRepository>, WalletAddress> repositorySetup = null)
        {
            var apiMock = new Mock<IInOutBlockchainApi>();
            apiSetup?.Invoke(apiMock);

            currency.BlockchainApi = apiMock.Object;

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

        public static IEnumerable<object[]> SendTestData =>
            new List<object[]>
            {
                new object[] {Common.CurrenciesTestNet.Get<Bitcoin>(), 0.001m, 0.0009m, 0.0001m, DustUsagePolicy.Warning},
                new object[] {Common.CurrenciesTestNet.Get<Litecoin>(), 0.0011m, 0.001m, 0.0001m, DustUsagePolicy.Warning}
            };

        [Theory]
        [MemberData(nameof(SendTestData))]
        public void SendTest(
            BitcoinBasedCurrency currency,
            decimal available,
            decimal amount,
            decimal fee,
            DustUsagePolicy dustUsagePolicy)
        {
            var error = Send(
                currency: currency,
                available: available,
                amount: amount,
                fee: fee,
                dustUsagePolicy: dustUsagePolicy,
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

        public static IEnumerable<object[]> SendDustAmountFailTestData =>
            new List<object[]>
            {
                new object[] {Common.CurrenciesTestNet.Get<Bitcoin>(), 0.001m, 0.0000009m, 0.0001m, DustUsagePolicy.Warning},
                new object[] {Common.CurrenciesTestNet.Get<Litecoin>(), 0.001m, 0.0000009m, 0.0001m, DustUsagePolicy.Warning}
            };

        [Theory]
        [MemberData(nameof(SendDustAmountFailTestData))]
        public void SendDustAmountFailTest(
            BitcoinBasedCurrency currency,
            decimal available,
            decimal amount,
            decimal fee,
            DustUsagePolicy dustUsagePolicy)
        {
            var error = Send(
                currency: currency,
                available: available,
                amount: amount,
                fee: fee,
                dustUsagePolicy: dustUsagePolicy);

            Assert.NotNull(error);
            Assert.Equal(Errors.InsufficientAmount, error.Code);
        }

        public static IEnumerable<object[]> SendInsufficientFundsFailTestData =>
            new List<object[]>
            {
                new object[] {Common.CurrenciesTestNet.Get<Bitcoin>(), 0.001m, 0.00090001m, 0.0001m, DustUsagePolicy.Warning},
                new object[] {Common.CurrenciesTestNet.Get<Litecoin>(), 0.0011m, 0.0010001m, 0.0001m, DustUsagePolicy.Warning}
            };

        [Theory]
        [MemberData(nameof(SendInsufficientFundsFailTestData))]
        public void SendInsufficientFundsFailTest(
            BitcoinBasedCurrency currency,
            decimal available,
            decimal amount,
            decimal fee,
            DustUsagePolicy dustUsagePolicy)
        {
            var error = Send(
                currency: currency,
                available: available,
                amount: amount,
                fee: fee,
                dustUsagePolicy: dustUsagePolicy);

            Assert.NotNull(error);
            Assert.Equal(Errors.InsufficientFunds, error.Code);
        }

        public static IEnumerable<object[]> SendDustChangeFailTestData =>
            new List<object[]>
            {
                new object[] {Common.CurrenciesTestNet.Get<Bitcoin>(), 0.001m, 0.0009m, 0.000095m, DustUsagePolicy.Warning},
                new object[] {Common.CurrenciesTestNet.Get<Litecoin>(), 0.001m, 0.0009m, 0.000095m, DustUsagePolicy.Warning}
            };

        [Theory]
        [MemberData(nameof(SendDustChangeFailTestData))]
        public void SendDustChangeFailTest(
            BitcoinBasedCurrency currency,
            decimal available,
            decimal amount,
            decimal fee,
            DustUsagePolicy dustUsagePolicy)
        {
            var error = Send(
                currency: currency,
                available: available,
                amount: amount,
                fee: fee,
                dustUsagePolicy: dustUsagePolicy);

            Assert.NotNull(error);
            Assert.Equal(Errors.InsufficientAmount, error.Code);
        }

        public static IEnumerable<object[]> SendDustAsAmountTestData =>
            new List<object[]>
            {
                new object[] {Common.CurrenciesTestNet.Get<Bitcoin>(), 0.001m, 0.0009m, 0.000095m, DustUsagePolicy.AddToDestination},
                new object[] {Common.CurrenciesTestNet.Get<Litecoin>(), 0.0011m, 0.001m, 0.0001m, DustUsagePolicy.AddToDestination}
            };

        [Theory]
        [MemberData(nameof(SendDustAsAmountTestData))]
        public void SendDustAsAmountTest(
            BitcoinBasedCurrency currency,
            decimal available,
            decimal amount,
            decimal fee,
            DustUsagePolicy dustUsagePolicy)
        {
            var change = available - amount - fee;

            var broadcastCallback = new Action<IBlockchainTransaction, CancellationToken>((tx, token) =>
            {
                var btcBasedTx = (IBitcoinBasedTransaction) tx;
                Assert.NotNull(btcBasedTx.Outputs.FirstOrDefault(o => o.Value == currency.CoinToSatoshi(amount + change)));
            });

            var error = Send(
                currency: currency,
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

        public static IEnumerable<object[]> SendDustAsFeeTestData =>
            new List<object[]>
            {
                new object[] {Common.CurrenciesTestNet.Get<Bitcoin>(), 0.001m, 0.0009m, 0.000095m, DustUsagePolicy.AddToFee},
                new object[] {Common.CurrenciesTestNet.Get<Litecoin>(), 0.0011m, 0.001m, 0.0001m, DustUsagePolicy.AddToFee}
            };

        [Theory]
        [MemberData(nameof(SendDustAsFeeTestData))]
        public void SendDustAsFeeTest(
            BitcoinBasedCurrency currency,
            decimal available,
            decimal amount,
            decimal fee,
            DustUsagePolicy dustUsagePolicy)
        {
            var change = available - amount - fee;

            var broadcastCallback = new Action<IBlockchainTransaction, CancellationToken>((tx, token) =>
            {
                var btcBasedTx = (IBitcoinBasedTransaction)tx;
                Assert.True(btcBasedTx.Fees == currency.CoinToSatoshi(fee + change));
            });

            var error = Send(
                currency: currency,
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