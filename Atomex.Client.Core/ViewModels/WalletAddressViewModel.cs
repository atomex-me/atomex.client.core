using System.Numerics;

using Atomex.Core;

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
        public BigInteger AvailableBalance { get; set; }
        public string CurrencyFormat { get; set; }
        public string CurrencyCode { get; set; }
        public bool IsFreeAddress { get; set; }
        public bool ShowTokenBalance { get; set; }
        public BigInteger TokenBalance { get; set; }
        public string TokenFormat { get; set; }
        public string TokenCode { get; set; }
        public int TokenId { get; set; }
        public bool IsTezosToken { get; set; }

        public BigInteger Balance => IsTezosToken ? TokenBalance : AvailableBalance;
        public string BalanceFormat => IsTezosToken ? TokenFormat : CurrencyFormat;
        public string BalanceCode => IsTezosToken ? TokenCode : CurrencyCode;
    }
}