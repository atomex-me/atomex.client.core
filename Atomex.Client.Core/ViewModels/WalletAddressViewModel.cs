using Atomex.Core;

namespace Atomex.ViewModels
{
    public class WalletAddressViewModel
    {
        public WalletAddress WalletAddress { get; set; }
        public string Address { get; set; }
        public bool HasActivity { get; set; }
        public decimal AvailableBalance { get; set; }
        public string CurrencyFormat { get; set; }
        public string CurrencyCode { get; set; }
        public bool IsFreeAddress { get; set; }
        public bool ShowTokenBalance { get; set; }
        public decimal TokenBalance { get; set; }
        public string TokenFormat { get; set; }
        public string TokenCode { get; set; }
        public decimal TokenId { get; set; }
        public bool IsTezosToken { get; set; }

        public decimal Balance => IsTezosToken ? TokenBalance : AvailableBalance;
        public string BalanceFormat => IsTezosToken ? TokenFormat : CurrencyFormat;
        public string BalanceCode => IsTezosToken ? TokenCode : CurrencyCode;
    }
}