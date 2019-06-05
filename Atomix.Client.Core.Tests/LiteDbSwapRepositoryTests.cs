using System;
using System.Security;
using System.Threading.Tasks;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Blockchain.Ethereum;
using Atomix.Common;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.LiteDb;
using Atomix.Swaps;
using Atomix.Swaps.Abstract;
using NBitcoin;
using Xunit;

namespace Atomix.Client.Core.Tests
{
    public class LiteDbSwapRepositoryTests
    {
        private const string PathToDb = "test.db";
        private SecureString Password { get; } = "12345".ToSecureString();

        private SwapState CreateSwap()
        {
            return new SwapState()
            {
                Order = new Order
                {
                    SwapId = Guid.NewGuid(),
                    Symbol = Symbols.EthBtc,
                    Side = Side.Buy,
                    SwapInitiative = true
                },
                Requisites = new SwapRequisites
                {
                    ToWallet = new WalletAddress
                    {
                        Currency = Currencies.Btc,
                        Address = "abcdefg"
                    },
                    RefundWallet = new WalletAddress
                    {
                        Currency = Currencies.Btc,
                        Address = "hijklmn"
                    }
                },
                Secret = new byte[] { 0x01, 0x02, 0x03 },
                SecretHash = new byte[] { 0x04, 0x05, 0x06 },
                StateFlags = SwapStateFlags.HasPayment | SwapStateFlags.HasPartyPayment,
                PaymentTx = new BitcoinBasedTransaction(
                    currency: Currencies.Btc,
                    tx: Transaction.Create(Network.TestNet)),

                PartyPaymentTx = new EthereumTransaction
                {
                    From = "abcdefghj",
                    To = "eprstifg"
                },
            };
        }

        [Fact]
        void NullPasswordTest()
        {
            Assert.Throws<ArgumentNullException>(() => {
                var repository = new LiteDbRepository(PathToDb, null);
            });
        }

        [Fact]
        public async Task<Guid> AddSwapTestAsync()
        {
            var repository = new LiteDbRepository(PathToDb, Password);

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

            var repository = new LiteDbRepository(PathToDb, Password);

            var swap = (SwapState)await repository
                .GetSwapByIdAsync(swapId)
                .ConfigureAwait(false);

            Assert.NotNull(swap);
            Assert.NotNull(swap.Order);
            Assert.NotNull(swap.Requisites);
            Assert.NotNull(swap.Secret);
            Assert.NotNull(swap.SecretHash);
            Assert.True(swap.StateFlags.HasFlag(SwapStateFlags.HasPayment));
            Assert.True(swap.StateFlags.HasFlag(SwapStateFlags.HasPartyPayment));
            Assert.NotNull(swap.PaymentTx);
            Assert.NotNull(swap.PartyPaymentTx);
        }

        [Fact]
        public async void RemoveSwapTest()
        {
            var swapId = await AddSwapTestAsync()
                .ConfigureAwait(false);

            var repository = new LiteDbRepository(PathToDb, Password);

            var swap = (SwapState)await repository
                .GetSwapByIdAsync(swapId)
                .ConfigureAwait(false);

            Assert.NotNull(swap);

            var result = await repository
                .RemoveSwapAsync(swap)
                .ConfigureAwait(false);

            Assert.True(result);
        }
    }
}