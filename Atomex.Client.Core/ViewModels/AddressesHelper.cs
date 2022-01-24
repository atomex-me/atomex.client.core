using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Atomex.Core;
using Atomex.TezosTokens;
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
            var isTezosToken = Currencies.IsTezosToken(currency.Name) || (tokenContract != null && tokenType != null);

            if (isTezosToken)
            {
                if (currency is Fa12Config fa12Config)
                {
                    if (tokenContract == null)
                        tokenContract = fa12Config.TokenContractAddress;

                    if (tokenType == null)
                        tokenType = "FA12";

                    currency = account.Currencies.Get<TezosConfig>("XTZ");
                }
            }

            // get all addresses with tokens (if exists)
            var addressesWithTokens = Currencies.HasTokens(currency.Name)
                ? (account.GetCurrencyAccount(currency.Name) is IHasTokens accountWithTokens
                    ? await accountWithTokens
                        .GetUnspentTokenAddressesAsync()
                        .ConfigureAwait(false) ?? new List<WalletAddress>()
                    : new List<WalletAddress>())
                : new List<WalletAddress>();

            var tokenSymbol = tokenContract != null
                ? addressesWithTokens.FirstOrDefault(a => a.TokenBalance?.Contract == tokenContract && a.TokenBalance?.Symbol != null)?.TokenBalance.Symbol
                : null;

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
                .Concat(addressesWithTokens)
                .Concat(new WalletAddress[] { freeAddress })
                .GroupBy(w => w.Address)
                .Select(g =>
                {
                    // main address
                    var address = g.FirstOrDefault(w => w.Currency == currency.Name);

                    var isFreeAddress = address?.Address == freeAddress.Address;

                    var hasAddressesWithTokens = g.Any(w => w.Currency != currency.Name);

                    var tokenContractAddresses = tokenContract != null
                        ? g.Where(w => w.TokenBalance?.Contract == tokenContract)
                        : Enumerable.Empty<WalletAddress>();

                    var isMultiTokenContract = tokenContractAddresses.Count() > 1;

                    var tokenAddress = tokenContractAddresses.FirstOrDefault();

                    var tokenBalance = isMultiTokenContract
                        ? tokenContractAddresses.Count() // for multi token contract balance is count of tokens
                        : tokenAddress?.Balance ?? 0m;

                    var showTokenBalance = isMultiTokenContract
                        ? tokenBalance != 0
                        : tokenContract != null && tokenAddress?.TokenBalance?.Symbol != null;

                    var tokenCode = isMultiTokenContract
                        ? "TOKENS"
                        : tokenAddress?.TokenBalance?.Symbol ?? tokenSymbol;

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
                                Decimals = 1,
                            },
                            HasActivity = address?.HasActivity ?? hasAddressesWithTokens
                        };

                    return new WalletAddressViewModel
                    {
                        WalletAddress    = walletAddress,
                        Address          = g.Key,
                        HasActivity      = address?.HasActivity ?? hasAddressesWithTokens,
                        AvailableBalance = address?.AvailableBalance() ?? 0m,
                        CurrencyFormat   = currency.Format,
                        CurrencyCode     = currency.Name,
                        IsFreeAddress    = isFreeAddress,
                        ShowTokenBalance = showTokenBalance,
                        TokenBalance     = tokenBalance,
                        TokenFormat      = "F8",
                        TokenCode        = tokenCode,
                        IsTezosToken     = isTezosToken
                    };
                });
        }
    }
}