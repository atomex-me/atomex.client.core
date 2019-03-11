using Atomix.Common.Proto;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.Swaps;

namespace Atomix.Api.Proto
{
    public class ExecutionReportScheme : ProtoScheme
    {
        public const int MessageId = 8;

        public ExecutionReportScheme()
            : base(MessageId)
        {
            Model.Add(typeof(Currency), true)
                .AddAvailableCurrencies();

            Model.Add(typeof(Symbol), true)
                .AddAvailableSymbols();

            Model.Add(typeof(WalletAddress), true)
                .AddRequired(nameof(WalletAddress.Currency))
                .AddRequired(nameof(WalletAddress.Address))
                .AddRequired(nameof(WalletAddress.PublicKey));

            Model.Add(typeof(Order), true)
                .AddRequired(nameof(Order.OrderId))
                .AddRequired(nameof(Order.ClientOrderId))
                .AddRequired(nameof(Order.Symbol))
                .AddRequired(nameof(Order.TimeStamp))
                .AddRequired(nameof(Order.Price))
                .AddRequired(nameof(Order.LastPrice))
                .AddRequired(nameof(Order.Qty))
                .AddRequired(nameof(Order.LeaveQty))
                .AddRequired(nameof(Order.LastQty))
                .AddRequired(nameof(Order.Fee))
                .AddRequired(nameof(Order.RedeemFee))
                .AddRequired(nameof(Order.Side))
                .AddRequired(nameof(Order.Type))
                .AddRequired(nameof(Order.Status))
                .AddRequired(nameof(Order.SwapId))
                .AddRequired(nameof(Order.SwapInitiative))
                .AddRequired(nameof(Order.FromWallets))
                .AddRequired(nameof(Order.ToWallet))
                .AddRequired(nameof(Order.RefundWallet))
                .AddRequired(nameof(Order.IsStayAfterDisconnect));

            Model.Add(typeof(SwapRequisites), true)
                .AddRequired(nameof(SwapRequisites.ToWallet))
                .AddRequired(nameof(SwapRequisites.RefundWallet));

            Model.Add(typeof(ExecutionReport), true)
                .AddRequired(nameof(Core.ExecutionReport.Order))
                .AddRequired(nameof(Core.ExecutionReport.Requisites));
        }
    }
}