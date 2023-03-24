using System.Numerics;
using Atomex.Wallets;

namespace Atomex.ViewModels
{
    public interface IWalletAddressViewModel
    {
        WalletAddress WalletAddress { get; set; }
    }

    public class WalletAddressViewModel : IWalletAddressViewModel
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
        public int TokenId { get; set; }
        public bool IsToken { get; set; }

        public decimal Balance => IsToken ? TokenBalance : AvailableBalance;
        public string BalanceFormat => IsToken ? TokenFormat : CurrencyFormat;
        public string BalanceCode => IsToken ? TokenCode : CurrencyCode;
    }
}