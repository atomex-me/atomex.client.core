using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Atomex.Core;
using Atomex.Wallet.Abstract;

namespace Atomex.ViewModels
{
    public static class AddressesHelper
    {
        public static async Task<IEnumerable<WalletAddressViewModel>> GetReceivingAddressesAsync(
            IAccount account,
            CurrencyConfig currency,
            string tokenContract = null,
            string tokenType = null)
        {
            // get all addresses with tokens (if exists)
            var tokenAddresses = Currencies.HasTokens(currency.Name)
                ? (account.GetCurrencyAccount(currency.Name) is IHasTokens accountWithTokens
                    ? await accountWithTokens
                        .GetUnspentTokenAddressesAsync()
                        .ConfigureAwait(false) ?? new List<WalletAddress>()
                    : new List<WalletAddress>())
                : new List<WalletAddress>();

            // get all nonzero addresses
            var activeAddresses = (await account
                .GetUnspentAddressesAsync(currency.Name)
                .ConfigureAwait(false))
                .ToList();

            // get free external address
            var freeAddress = await account
                .GetFreeExternalAddressAsync(currency.Name)
                .ConfigureAwait(false);

            return activeAddresses
                .Concat(tokenAddresses)
                .Concat(new WalletAddress[] { freeAddress })
                .GroupBy(w => w.Address)
                .Select(g =>
                {
                    // main address
                    var address = g.FirstOrDefault(w => w.Currency == currency.Name);

                    var isFreeAddress = address?.Address == freeAddress.Address;

                    var hasTokens = g.Any(w => w.Currency != currency.Name);

                    var tokenAddresses = tokenContract != null
                        ? g.Where(w => w.TokenBalance?.Contract == tokenContract)
                        : Enumerable.Empty<WalletAddress>();

                    var hasSeveralTokens = tokenAddresses.Count() > 1;

                    var tokenAddress = tokenAddresses.FirstOrDefault();

                    var tokenBalance = hasSeveralTokens
                        ? tokenAddresses.Count()
                        : tokenAddress?.Balance ?? 0m;

                    var showTokenBalance = hasSeveralTokens
                        ? tokenBalance != 0
                        : tokenContract != null && tokenAddress?.TokenBalance?.Symbol != null;

                    var tokenCode = hasSeveralTokens
                        ? "TOKENS"
                        : tokenAddress?.TokenBalance?.Symbol ?? "";

                    var walletAddress = tokenContract == null
                        ? address
                        : tokenAddress ?? new WalletAddress
                        {
                            Address  = g.Key,
                            KeyIndex = g.First().KeyIndex,
                            KeyType  = g.First().KeyType,
                            Balance  = 0m,
                            Currency = tokenType,
                            TokenBalance = new Blockchain.Tezos.TokenBalance
                            {
                                Contract = tokenContract,
                                Balance  = "0",
                                Decimals = 1
                            },
                            HasActivity = address?.HasActivity ?? hasTokens
                        };

                    return new WalletAddressViewModel
                    {
                        WalletAddress    = walletAddress,
                        Address          = g.Key,
                        HasActivity      = address?.HasActivity ?? hasTokens,
                        AvailableBalance = address?.AvailableBalance() ?? 0m,
                        CurrencyFormat   = currency.Format,
                        CurrencyCode     = currency.Name,
                        IsFreeAddress    = isFreeAddress,
                        ShowTokenBalance = showTokenBalance,
                        TokenBalance     = tokenBalance,
                        TokenFormat      = "F8",
                        TokenCode        = tokenCode
                    };
                });
        }
    }
}