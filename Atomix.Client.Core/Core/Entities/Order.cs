using System;
using System.Collections.Generic;
using Atomix.Common.Json;
using Newtonsoft.Json;

namespace Atomix.Core.Entities
{
    public class Order
    {
        public long Id { get; set; }
        public Guid OrderId { get; set; }
        public string ClientOrderId { get; set; }
        public string UserId { get; set; }
        public int SymbolId { get; set; }
        public Symbol Symbol { get; set; }
        public DateTime TimeStamp { get; set; }
        public decimal Price { get; set; }
        public decimal LastPrice { get; set; }
        public decimal Qty { get; set; }
        public decimal LeaveQty { get; set; }
        public decimal LastQty { get; set; }
        public decimal Fee { get; set; }
        public decimal RedeemFee { get; set; }
        public Side Side { get; set; }
        public OrderType Type { get; set; }
        public OrderStatus Status { get; set; }
        public bool EndOfTransaction { get; set; }
        public Guid SwapId { get; set; }
        public bool SwapInitiative { get; set; }
        public bool IsStayAfterDisconnect { get; set; }
        public bool IsApproved { get; set; }

        public IList<WalletAddress> FromWallets { get; set; }
        public WalletAddress ToWallet { get; set; }
        public WalletAddress RefundWallet { get; set; }

        public IList<OrderWallet> Wallets { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, new CompactCurrencyConverter(), new CompactSymbolConverter());
        }

        public Order Clone()
        {
            return (Order)MemberwiseClone();
        }

        public string ToCompactString()
        {
            return $"{{\"OrderId\": \"{OrderId}\", " +
                   $"\"ClientOrderId\": \"{ClientOrderId}\", " +
                   $"\"Symbol\": \"{Symbol?.Name}\", " +
                   $"\"Price\": \"{Price}\", " +
                   $"\"Qty\": \"{Qty}\", " +
                   $"\"Fee\": \"{Fee}\", " +
                   $"\"Side\": \"{Side}\", " +
                   $"\"Type\": \"{Type}\", " +
                   $"\"Status\": \"{Status}\", " +
                   $"\"EndOfTransaction\": \"{EndOfTransaction}\", " +
                   $"\"SwapId\": \"{SwapId}\", " +
                   $"\"IsStayAfterDisconnect\": \"{IsStayAfterDisconnect}\"}}";
        }

        public string ToMarketDataCompactString()
        {
            return $"{{\"Symbol\": \"{Symbol?.Name}\", " +
                   $"\"Price\": \"{Price}\", " +
                   $"\"LastPrice\": \"{LastPrice}\", " +
                   $"\"Qty\": \"{Qty}\", " +
                   $"\"LeaveQty\": \"{LeaveQty}\", " +
                   $"\"LastQty\": \"{LastQty}\", " +
                   $"\"Side\": \"{Side}\", " +
                   $"\"Type\": \"{Type}\", " +
                   $"\"Status\": \"{Status}\", " +
                   $"\"EndOfTransaction\": \"{EndOfTransaction}\"}}";
        }
    }
}