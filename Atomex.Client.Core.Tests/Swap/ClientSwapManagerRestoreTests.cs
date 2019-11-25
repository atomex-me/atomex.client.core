//using System;
//using System.Collections.Generic;
//using System.Text;
//using System.Threading.Tasks;
//using Atomex.Core.Entities;
//using Atomex.Swaps;
//using Atomex.Swaps.Abstract;
//using Atomex.Wallet;
//using Atomex.Wallet.Abstract;
//using Moq;
//using Xunit;

//namespace Atomex.Client.Core.Tests
//{
//    public class ClientSwapManagerRestoreTests
//    {
//        [Fact]
//        public async void RestoreSwapsAsyncTest()
//        {
//            var swaps = new List<ClientSwap>()
//            {
//                new ClientSwap()
//                {
//                    Id = 1000,
//                    Status = SwapStatus.Accepted,
//                    StateFlags = SwapStateFlags.Empty
//                }
//            };

//            var accountMock = new Mock<IAccount>();
//            accountMock.SetupGet(a => a.Currencies).Returns(Common.CurrenciesTestNet);
//            accountMock.Setup(a => a.GetSwapsAsync()).Returns(Task.FromResult((IEnumerable<ClientSwap>)swaps));
//            accountMock.Setup(a => a.UpdateSwapAsync(It.IsAny<ClientSwap>())).Returns(Task.FromResult(true));
                
//            var swapClientMock = new Mock<ISwapClient>();

//            var swapManager = new ClientSwapManager(accountMock.Object, swapClientMock.Object);

//            await swapManager.RestoreSwapsAsync();


//        }
//    }
//}