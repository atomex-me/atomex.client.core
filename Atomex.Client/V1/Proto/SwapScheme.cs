using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Proto
{
    public class SwapScheme : ProtoScheme<Response<Swap>>
    {
        public SwapScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Swap), true)
                .AddRequired(nameof(Swap.Id))
                .AddRequired(nameof(Swap.Status))
                .AddRequired(nameof(Swap.SecretHash))
                .AddRequired(nameof(Swap.TimeStamp))
                .AddRequired(nameof(Swap.OrderId))
                .AddRequired(nameof(Swap.Symbol))
                .AddRequired(nameof(Swap.Side))
                .AddRequired(nameof(Swap.Price))
                .AddRequired(nameof(Swap.Qty))
                .AddRequired(nameof(Swap.IsInitiative))
                .AddRequired(nameof(Swap.ToAddress))
                .AddRequired(nameof(Swap.RewardForRedeem))
                .AddRequired(nameof(Swap.PaymentTxId))
                .AddRequired(nameof(Swap.RedeemScript))
                .AddRequired(nameof(Swap.RefundAddress))
                .AddRequired(nameof(Swap.PartyAddress))
                .AddRequired(nameof(Swap.PartyRewardForRedeem))
                .AddRequired(nameof(Swap.PartyPaymentTxId))
                .AddRequired(nameof(Swap.PartyRedeemScript))
                .AddRequired(nameof(Swap.PartyRefundAddress));

            Model.Add(typeof(Response<Swap>), true)
                .AddRequired(nameof(Response<Swap>.RequestId))
                .AddRequired(nameof(Response<Swap>.Data))
                .AddRequired(nameof(Response<Swap>.EndOfMessage));
        }
    }
}