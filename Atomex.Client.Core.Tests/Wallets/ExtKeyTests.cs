using Xunit;

namespace Atomex.Wallets
{
    public abstract class ExtKeyTests<T> : KeyTests<T> where T : IExtKey
    {
        [Fact]
        public void CanDeriveWithKeyPath()
        {
            using var key = CreateKey(KeySize, out var _);

            var derivedKey = key.Derive("m/0'/0'/0'/0/0");

            Assert.NotNull(derivedKey);
        }

        [Fact]
        public void CanDeriveWithIndex()
        {
            using var key = CreateKey(KeySize, out var _);

            var derivedKey = key.Derive(10);

            Assert.NotNull(derivedKey);
        }
    }
}