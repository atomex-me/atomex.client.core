using Atomix.Common.Proto;
using Atomix.Core.Entities;
using Atomix.Swaps;

namespace Atomix.Api.Proto
{
    public class SwapDataScheme : ProtoScheme
    {
        public const int MessageId = 9;

        public SwapDataScheme()
            : base(MessageId)
        {
            Model.Add(typeof(Currency), true)
                .AddAvailableCurrencies();

            Model.Add(typeof(Symbol), true)
                .AddAvailableSymbols();

            Model.Add(typeof(SwapData), true)
                .AddRequired(nameof(SwapData.SwapId))
                .AddRequired(nameof(SwapData.Symbol))
                .AddRequired(nameof(SwapData.Type))
                .AddRequired(nameof(SwapData.Data));
        }
    }
}