using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Moq;
using NBitcoin;
using Xunit;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;
using Network = Atomex.Core.Network;

namespace Atomex.Client.Core.Tests
{
    public class BitcoinBasedCurrencyAccountSendTests
    {
        private IEnumerable<BitcoinBasedTxOutput> GetOutputs(string address, NBitcoin.Network network, params long[] values)
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
            BitcoinBasedConfig currency,
            decimal available,
            decimal amount,
            decimal fee,
            DustUsagePolicy dustUsagePolicy,
            Action<Mock<IBlockchainApi>> apiSetup = null,
            Action<Mock<IAccountDataRepository>, WalletAddress> repositorySetup = null)
        {
            var apiMock = new Mock<IBlockchainApi>();
            apiSetup?.Invoke(apiMock);

            var wallet = new HdWallet(Network.TestNet);
            var fromAddress = wallet.GetAddress(
                currency: currency,
                account: 0,
                chain: 0,
                index: 0,
                keyType: CurrencyConfig.StandardKey);
            var fromOutputs = GetOutputs(fromAddress.Address, NBitcoin.Network.TestNet, currency.CoinToSatoshi(available)).ToList();

            var repositoryMock = new Mock<IAccountDataRepository>();
            repositorySetup?.Invoke(repositoryMock, fromAddress);

            var currencies = Common.CurrenciesTestNet;
            currencies.GetByName(currency.Name).BlockchainApi = apiMock.Object;

            var account = new BitcoinBasedAccount(
                currency: currency.Name,
                currencies: currencies,
                wallet: wallet,
                dataRepository: repositoryMock.Object);

            return account
                .SendAsync(
                    from: fromOutputs,
                    to: currency.TestAddress(),
                    amount: amount,
                    fee: fee,
                    dustUsagePolicy: dustUsagePolicy)
                .WaitForResult();
        }

        public static IEnumerable<object[]> SendTestData =>
            new List<object[]>
            {
                new object[] {Common.CurrenciesTestNet.Get<BitcoinConfig>("BTC"), 0.001m, 0.0009m, 0.0001m, DustUsagePolicy.Warning},
                new object[] {Common.CurrenciesTestNet.Get<LitecoinConfig>("LTC"), 0.0011m, 0.001m, 0.0001m, DustUsagePolicy.Warning}
            };

        [Theory]
        [MemberData(nameof(SendTestData))]
        public void SendTest(
            BitcoinBasedConfig currency,
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
                    apiMock.Setup(a => a.TryBroadcastAsync(It.IsAny<IBlockchainTransaction>(), 3, 1000, CancellationToken.None))
                        .Returns(Task.FromResult(new Result<string>("<txid>")));
                },
                repositorySetup: (repositoryMock, fromAddress) =>
                {
                    repositoryMock.Setup(r => r.GetWalletAddressAsync(It.IsAny<string>(), fromAddress.Address))
                        .Returns(Task.FromResult(fromAddress));
                });

            Assert.Null(error);
        }

        public static IEnumerable<object[]> SendDustAmountFailTestData =>
            new List<object[]>
            {
                new object[] {Common.CurrenciesTestNet.Get<BitcoinConfig>("BTC"), 0.001m, 0.0000009m, 0.0001m, DustUsagePolicy.Warning},
                new object[] {Common.CurrenciesTestNet.Get<LitecoinConfig>("LTC"), 0.001m, 0.0000009m, 0.0001m, DustUsagePolicy.Warning}
            };

        [Theory]
        [MemberData(nameof(SendDustAmountFailTestData))]
        public void SendDustAmountFailTest(
            BitcoinBasedConfig currency,
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
                new object[] {Common.CurrenciesTestNet.Get<BitcoinConfig>("BTC"), 0.001m, 0.00090001m, 0.0001m, DustUsagePolicy.Warning},
                new object[] {Common.CurrenciesTestNet.Get<LitecoinConfig>("LTC"), 0.0011m, 0.0010001m, 0.0001m, DustUsagePolicy.Warning}
            };

        [Theory]
        [MemberData(nameof(SendInsufficientFundsFailTestData))]
        public void SendInsufficientFundsFailTest(
            BitcoinBasedConfig currency,
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
                new object[] {Common.CurrenciesTestNet.Get<BitcoinConfig>("BTC"), 0.001m, 0.0009m, 0.000095m, DustUsagePolicy.Warning},
                new object[] {Common.CurrenciesTestNet.Get<LitecoinConfig>("LTC"), 0.001m, 0.0009m, 0.000095m, DustUsagePolicy.Warning}
            };

        [Theory]
        [MemberData(nameof(SendDustChangeFailTestData))]
        public void SendDustChangeFailTest(
            BitcoinBasedConfig currency,
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
                new object[] {Common.CurrenciesTestNet.Get<BitcoinConfig>("BTC"), 0.001m, 0.0009m, 0.000095m, DustUsagePolicy.AddToDestination},
                new object[] {Common.CurrenciesTestNet.Get<LitecoinConfig>("LTC"), 0.0011m, 0.001m, 0.0001m, DustUsagePolicy.AddToDestination}
            };

        [Theory]
        [MemberData(nameof(SendDustAsAmountTestData))]
        public void SendDustAsAmountTest(
            BitcoinBasedConfig currency,
            decimal available,
            decimal amount,
            decimal fee,
            DustUsagePolicy dustUsagePolicy)
        {
            var change = available - amount - fee;

            var broadcastCallback = new Action<IBlockchainTransaction, int, int, CancellationToken>((tx, attempts, attemptsInterval, token) =>
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
                    apiMock.Setup(a => a.TryBroadcastAsync(It.IsAny<IBlockchainTransaction>(), 3, 1000, CancellationToken.None))
                        .Callback(broadcastCallback)
                        .Returns(Task.FromResult(new Result<string>("<txid>")));
                },
                repositorySetup: (repositoryMock, fromAddress) =>
                {
                    repositoryMock.Setup(r => r.GetWalletAddressAsync(It.IsAny<string>(), fromAddress.Address))
                        .Returns(Task.FromResult(fromAddress));
                });

            Assert.Null(error);
        }

        public static IEnumerable<object[]> SendDustAsFeeTestData =>
            new List<object[]>
            {
                new object[] {Common.CurrenciesTestNet.Get<BitcoinConfig>("BTC"), 0.001m, 0.0009m, 0.000095m, DustUsagePolicy.AddToFee},
                new object[] {Common.CurrenciesTestNet.Get<LitecoinConfig>("LTC"), 0.0011m, 0.001m, 0.0001m, DustUsagePolicy.AddToFee}
            };

        [Theory]
        [MemberData(nameof(SendDustAsFeeTestData))]
        public void SendDustAsFeeTest(
            BitcoinBasedConfig currency,
            decimal available,
            decimal amount,
            decimal fee,
            DustUsagePolicy dustUsagePolicy)
        {
            var change = available - amount - fee;

            var broadcastCallback = new Action<IBlockchainTransaction, int, int, CancellationToken>((tx, attempts, attemptsInterval, token) =>
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
                    apiMock.Setup(a => a.TryBroadcastAsync(It.IsAny<IBlockchainTransaction>(), 3, 1000, CancellationToken.None))
                        .Callback(broadcastCallback)
                        .Returns(Task.FromResult(new Result<string>("<txid>")));
                },
                repositorySetup: (repositoryMock, fromAddress) =>
                {
                    repositoryMock.Setup(r => r.GetWalletAddressAsync(It.IsAny<string>(), fromAddress.Address))
                        .Returns(Task.FromResult(fromAddress));
                });

            Assert.Null(error);
        }
    }
}