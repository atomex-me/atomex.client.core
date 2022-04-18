using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Atomex.Common;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public enum TestEnum
    {
        [Description("transaction")]
        Transaction,

        [Description("set_deposits_limit")]
        SetDepositsLimit,

        Origination,
    }

    public struct TestNotEnum
    {

    }


    public class EnumExtensionsTests
    {
        [Fact]
        public void GetDescription_OnNotEnum_ThrowsException()
        {
            var notEnum = new TestNotEnum();

            Assert.Throws<ArgumentException>(() => notEnum.GetDescription());
        }

        [Fact]
        public void GetDescription_OnEnumWithDescription_ReturnsDescription()
        {
            var transactionDescription = TestEnum.Transaction.GetDescription();
            var setDepositsLimitsDescription = TestEnum.SetDepositsLimit.GetDescription();

            Assert.Equal("transaction", transactionDescription);
            Assert.Equal("set_deposits_limit", setDepositsLimitsDescription);
        }

        [Fact]
        public void GetDescription_OnEnumWithoutDescription_ReturnsToString()
        {
            var originationDescription = TestEnum.Origination.GetDescription();

            Assert.Equal("Origination", originationDescription);
        }
    }
}
