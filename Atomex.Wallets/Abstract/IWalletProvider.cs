namespace Atomex.Wallets.Abstract
{
    public interface IWalletProvider
    {
        IWallet GetWallet(WalletInfo walletInfo);
    }
}