namespace Atomex.Wallets.Abstract
{
    public interface ICurrencyConfigProvider
    {
        CurrencyConfig GetByName(string currency);
        T GetByName<T>(string currency);
    }
}