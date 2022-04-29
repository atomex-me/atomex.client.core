using Netezos.Encoding;
using Xunit;

using Atomex.Blockchain.Tezos.Common;

namespace Atomex.Blockchain.Tezos
{
    public class MichelineExtensionsTests
    {
        [Theory]
        [InlineData(
            "{\"entrypoint\":\"initiate\",\"value\":{\"prim\":\"Pair\",\"args\":[{\"string\":\"tz1fyFNErtQnHZz6WujUTKzv4dZsqEggAW4P\"},{\"prim\":\"Pair\",\"args\":[{\"prim\":\"Pair\",\"args\":[{\"bytes\":\"b53fa104ff20439c20c81e281b0dbc332d728d4e8ea4e8063699ff2964e69790\"},{\"int\":\"1568416914\"}]},{\"int\":\"0\"}]}]}}",
            "{\"prim\":\"Pair\",\"args\":[{\"string\":\"tz1fyFNErtQnHZz6WujUTKzv4dZsqEggAW4P\"},{\"prim\":\"Pair\",\"args\":[{\"prim\":\"Pair\",\"args\":[{\"bytes\":\"b53fa104ff20439c20c81e281b0dbc332d728d4e8ea4e8063699ff2964e69790\"},{\"int\":\"1568416914\"}]},{\"int\":\"0\"}]}]}")]
        [InlineData(
            "{\"entrypoint\":\"redeem\",\"value\":{\"bytes\":\"b0b1d06f67458eb567ac242503fc623398d2f98d92c99c92d3193ffd6ce0d850\"}}",
            "{\"bytes\":\"b0b1d06f67458eb567ac242503fc623398d2f98d92c99c92d3193ffd6ce0d850\"}")]
        [InlineData(
            "{\"entrypoint\":\"refund\",\"value\":{\"bytes\":\"9618c4775d52a802065003b6591cc9ee73ca6b9d39fd17a9d6acdc4eb566fafb\"}}",
            "{\"bytes\":\"9618c4775d52a802065003b6591cc9ee73ca6b9d39fd17a9d6acdc4eb566fafb\"}")]
        [InlineData(
            "{\"entrypoint\":\"transfer\",\"value\":{\"prim\":\"Pair\",\"args\":[{\"bytes\":\"014464fe47d0f512ce10ca5ea7afb30a330b48a2d600\"},{\"prim\":\"Pair\",\"args\":[{\"bytes\":\"0000a106711b8a84a7c7fec3c2e9e3c70480287ffa95\"},{\"int\":\"100287268974000000000\"}]}]}}",
            "{\"prim\":\"Pair\",\"args\":[{\"bytes\":\"014464fe47d0f512ce10ca5ea7afb30a330b48a2d600\"},{\"prim\":\"Pair\",\"args\":[{\"bytes\":\"0000a106711b8a84a7c7fec3c2e9e3c70480287ffa95\"},{\"int\":\"100287268974000000000\"}]}]}")]
        public void CanExtractMichelineValueTest(string parameters, string value)
        {
            var extractedValue = MichelineExtensions.ExtractMichelineValue(parameters);

            Assert.Equal(value, extractedValue);

            var micheline = Micheline.FromJson(value);

            Assert.NotNull(micheline);
        }

        [Theory]
        [InlineData("{\"entrypoint\":\"initiate\",\"value\":{\"prim\":\"Pair\",\"args\":[{\"string\":\"tz1fyFNErtQnHZz6WujUTKzv4dZsqEggAW4P\"},{\"prim\":\"Pair\",\"args\":[{\"prim\":\"Pair\",\"args\":[{\"bytes\":\"b53fa104ff20439c20c81e281b0dbc332d728d4e8ea4e8063699ff2964e69790\"},{\"int\":\"1568416914\"}]},{\"int\":\"0\"}]}]}}")]
        [InlineData("{\"entrypoint\":\"redeem\",\"value\":{\"bytes\":\"b0b1d06f67458eb567ac242503fc623398d2f98d92c99c92d3193ffd6ce0d850\"}}")]
        [InlineData("{\"entrypoint\":\"refund\",\"value\":{\"bytes\":\"9618c4775d52a802065003b6591cc9ee73ca6b9d39fd17a9d6acdc4eb566fafb\"}}")]
        [InlineData("{\"entrypoint\":\"transfer\",\"value\":{\"prim\":\"Pair\",\"args\":[{\"bytes\":\"014464fe47d0f512ce10ca5ea7afb30a330b48a2d600\"},{\"prim\":\"Pair\",\"args\":[{\"bytes\":\"0000a106711b8a84a7c7fec3c2e9e3c70480287ffa95\"},{\"int\":\"100287268974000000000\"}]}]}}")]
        public void TryExtractMichelineValueTest(string parameters)
        {
            var micheline = MichelineExtensions.TryExtractMichelineValue(parameters);

            Assert.NotNull(micheline);
        }
    }
}