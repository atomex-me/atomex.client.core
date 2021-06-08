using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Tezos;
using Atomex.Wallet.Abstract;
using Atomex.Core;

namespace Atomex.Wallet.Tezos
{
    public class TezosTokensScanner : ICurrencyHdWalletScanner
    {
        private readonly TezosAccount _tezosAccount;

        public TezosTokensScanner(TezosAccount tezosAccount)
        {
            _tezosAccount = tezosAccount ?? throw new ArgumentNullException(nameof(tezosAccount));
        }

        public Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task ScanContractAsync(
            string contractAddress,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task ScanContractAsync(
            string address,
            string contractAddress,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var tezosConfig = _tezosAccount.Config;

                var xtzAddress = await _tezosAccount
                    .GetAddressAsync(address, cancellationToken)
                    .ConfigureAwait(false);

                var bcdSettings = tezosConfig.BcdApiSettings;

                var bcdApi = new BcdApi(bcdSettings);

                var tokenBalancesResult = await bcdApi
                    .GetTokenBalancesAsync(address, contractAddress, cancellationToken)
                    .ConfigureAwait(false);

                if (tokenBalancesResult.HasError)
                {
                    Log.Error($"Error while scan tokens balance for " +
                        $"contract: {contractAddress} and " +
                        $"address: {address}. " +
                        $"Code: {tokenBalancesResult.Error.Code}. " +
                        $"Description: {tokenBalancesResult.Error.Description}");

                    return;
                }

                var localTokenAddresses = (await _tezosAccount.DataRepository
                    .GetTezosTokenAddressesAsync(address, contractAddress)
                    .ConfigureAwait(false))
                    .ToList();

                var tokenBalanceDict = tokenBalancesResult.Value
                    .ToDictionary(tb => $"{tb.Contract}:{tb.TokenId}", tb => tb);

                foreach (var localTokenAddress in localTokenAddresses)
                {
                    var tokenId = localTokenAddress.Currency; // {contract}:{tokenId}

                    if (tokenBalanceDict.TryGetValue(tokenId, out var tb))
                    {
                        localTokenAddress.Balance = tb.GetTokenBalance();

                        tokenBalanceDict.Remove(tokenId);
                    }
                    else
                    {
                        localTokenAddress.Balance = 0;
                        // todo: may be remove zero address from db?
                    }
                }

                var newTokenAddresses = tokenBalanceDict.Values.Select(tb => new WalletAddress
                {
                    Address     = address,
                    Balance     = tb.GetTokenBalance(),
                    Currency    = $"{tb.Contract}:{tb.TokenId}",
                    KeyIndex    = xtzAddress.KeyIndex,
                    HasActivity = true
                });

                localTokenAddresses.AddRange(newTokenAddresses);

                await _tezosAccount.DataRepository
                    .UpsertTezosTokenAddressesAsync(localTokenAddresses)
                    .ConfigureAwait(false);

                // scan transfers



            }, cancellationToken);
        }
    }
}