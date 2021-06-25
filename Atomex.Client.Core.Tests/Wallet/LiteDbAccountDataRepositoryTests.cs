using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Blockchain.Ethereum;
using Atomex.Common;
using Atomex.Core;
using Atomex.LiteDb;
using NBitcoin;
using Xunit;
using Network = Atomex.Core.Network;

namespace Atomex.Client.Core.Tests
{
    public class LiteDbAccountDataRepositoryTests
    {
        private const string PathToDb = "test.db";
        private SecureString Password { get; } = "12345".ToSecureString();

        private long _id;
        private DateTime UtcNow { get; } = DateTime.UtcNow;

        private Swap CreateSwap()
        {
            var id = Interlocked.Increment(ref _id);

            return new Swap
            {
                Id         = id,
                Secret     = new byte[] { 0x01, 0x02, 0x03 },
                StateFlags = SwapStateFlags.HasPayment | SwapStateFlags.HasPartyPayment,
                TimeStamp  = UtcNow,
                Symbol     = "ETH/BTC",
                Side       = Side.Buy,
                SecretHash = new byte[] { 0x04, 0x05, 0x06 },

                PaymentTx = new BitcoinBasedTransaction(
                    currency: Common.BtcTestNet.Name,
                    tx: Transaction.Create(NBitcoin.Network.TestNet)),

                PartyPaymentTx = new EthereumTransaction()
                {
                    Currency = Common.EthTestNet.Name,
                    From     = "abcdefghj",
                    To       = "eprstifg"
                }
            };
        }

        [Fact]
        public void NullPasswordTest()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                var _ = new LiteDbAccountDataRepository(
                    pathToDb: PathToDb,
                    password: null,
                    currencies: Common.CurrenciesTestNet,
                    network: Network.TestNet);
            });
        }

        [Fact]
        public async Task<long> AddSwapTestAsync()
        {
            File.Delete(PathToDb);

            var repository = new LiteDbAccountDataRepository(
                pathToDb: PathToDb,
                password: Password,
                currencies: Common.CurrenciesTestNet,
                network: Network.TestNet);

            var swap = CreateSwap();

            var result = await repository
                .AddSwapAsync(swap)
                .ConfigureAwait(false);

            Assert.True(result);

            return swap.Id;
        }

        [Fact]
        public async void GetSwapByIdTest()
        {
            var swapId = await AddSwapTestAsync()
                .ConfigureAwait(false);

            var repository = new LiteDbAccountDataRepository(
                pathToDb: PathToDb,
                password: Password,
                currencies: Common.CurrenciesTestNet,
                network: Network.TestNet);

            var swap = await repository
                .GetSwapByIdAsync(swapId)
                .ConfigureAwait(false);

            Assert.NotNull(swap);
            Assert.NotNull(swap.Symbol);
            Assert.NotNull(swap.Secret);
            Assert.NotNull(swap.SecretHash);
            Assert.NotEqual(swap.TimeStamp, UtcNow);
            Assert.True(swap.StateFlags.HasFlag(SwapStateFlags.HasPayment));
            Assert.True(swap.StateFlags.HasFlag(SwapStateFlags.HasPartyPayment));
            Assert.NotNull(swap.PaymentTx);
            Assert.NotNull(swap.PartyPaymentTx);
        }

        [Fact]
        public async void AddEthereumTransactionTest()
        {
            var repository = new LiteDbAccountDataRepository(
                pathToDb: PathToDb,
                password: Password,
                currencies: Common.CurrenciesTestNet,
                network: Network.TestNet);

            var id = "abcdefgh";

            var tx = new EthereumTransaction
            {
                Id          = id,
                Currency    = Common.EthTestNet.Name,
                InternalTxs = new List<EthereumTransaction>
                {
                    new EthereumTransaction {Currency = Common.EthTestNet.Name}
                }
            };

            var result = await repository
                .UpsertTransactionAsync(tx)
                .ConfigureAwait(false);

            Assert.True(result);

            var readTx = await repository
                .GetTransactionByIdAsync(Common.EthTestNet.Name, id, Common.EthTestNet.TransactionType)
                .ConfigureAwait(false) as EthereumTransaction;

            Assert.NotNull(readTx);
            Assert.NotNull(readTx.InternalTxs);
            Assert.Equal(id, readTx.Id);
        }
    }
}