using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Blockchain.Ethereum;
using Atomix.Common;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.LiteDb;
using NBitcoin;
using Xunit;

namespace Atomix.Client.Core.Tests
{
    public class LiteDbAccountDataRepositoryTests
    {
        private const string PathToDb = "test.db";
        private SecureString Password { get; } = "12345".ToSecureString();

        private long _id;
        private DateTime UtcNow { get; } = DateTime.UtcNow;

        private ClientSwap CreateSwap()
        {
            var id = Interlocked.Increment(ref _id);

            return new ClientSwap
            {
                Id = id,
                Secret = new byte[] { 0x01, 0x02, 0x03 },
                StateFlags = SwapStateFlags.HasPayment | SwapStateFlags.HasPartyPayment,
                TimeStamp = UtcNow,
                Symbol = Common.EthBtcTestNet,
                Side = Side.Buy,
                SecretHash = new byte[] { 0x04, 0x05, 0x06 },

                PaymentTx = new BitcoinBasedTransaction(
                    currency: Common.BtcTestNet,
                    bytes: Transaction.Create(NBitcoin.Network.TestNet).ToBytes()),

                PartyPaymentTx = new EthereumTransaction(Common.EthTestNet)
                {
                    From = "abcdefghj",
                    To = "eprstifg"
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
                    symbols: Common.SymbolsTestNet);
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
                symbols: Common.SymbolsTestNet);

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
                symbols: Common.SymbolsTestNet);

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
    }
}