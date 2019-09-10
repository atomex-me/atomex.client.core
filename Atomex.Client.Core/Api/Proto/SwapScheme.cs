using Atomex.Abstract;
using Atomex.Common.Proto;
using Atomex.Core;
using Atomex.Core.Entities;

namespace Atomex.Api.Proto
{
    public class SwapScheme : ProtoScheme<Response<ClientSwap>>
    {
        public SwapScheme(byte messageId, ICurrencies currencies)
            : base(messageId)
        {
            Model.Add(typeof(Currency), true)
                .AddCurrencies(currencies)
                .AddRequired(nameof(Currency.Name));

            Model.Add(typeof(Symbol), true)
                .AddRequired(nameof(Symbol.Name));

            Model.Add(typeof(ClientSwap), true)
                .AddRequired(nameof(ClientSwap.Id))
                .AddRequired(nameof(ClientSwap.Status))
                .AddRequired(nameof(ClientSwap.SecretHash))
                .AddRequired(nameof(ClientSwap.TimeStamp))
                .AddRequired(nameof(ClientSwap.OrderId))
                .AddRequired(nameof(ClientSwap.Symbol))
                .AddRequired(nameof(ClientSwap.Side))
                .AddRequired(nameof(ClientSwap.Price))
                .AddRequired(nameof(ClientSwap.Qty))
                .AddRequired(nameof(ClientSwap.IsInitiative))
                .AddRequired(nameof(ClientSwap.ToAddress))
                .AddRequired(nameof(ClientSwap.RewardForRedeem))
                .AddRequired(nameof(ClientSwap.PaymentTxId))
                .AddRequired(nameof(ClientSwap.PartyAddress))
                .AddRequired(nameof(ClientSwap.PartyRewardForRedeem))
                .AddRequired(nameof(ClientSwap.PartyPaymentTxId));

            Model.Add(typeof(Response<ClientSwap>), true)
                .AddRequired(nameof(Response<ClientSwap>.RequestId))
                .AddRequired(nameof(Response<ClientSwap>.Data))
                .AddRequired(nameof(Response<ClientSwap>.EndOfMessage));
        }
    }
}