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
            return Task.Run(async () =>
            {
                //var tezosConfig = _tezosAccount.Config;

                //var bcdSettings = tezosConfig.BcdApiSettings;

                //var bcdApi = new BcdApi(bcdSettings);

                //var tokenBalancesCountResult = await bcdApi
                //    .GetTokenBalancesCountAsync(address, cancellationToken)
                //    .ConfigureAwait(false);

                //var tokenBalancesCount = tokenBalancesCountResult.Value;

                //foreach (var contract in tokenBalancesCount.Keys)
                //{
                //    var tokenBalances = await bcdApi
                //        .GetTokenBalancesAsync(address, contract, cancellationToken)
                //        .ConfigureAwait(false);


                //}

            }, cancellationToken);
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

                var tokenContractsResult = await bcdApi
                    .GetTokenContractsAsync(address, cancellationToken)
                    .ConfigureAwait(false);

                if (tokenContractsResult.HasError)
                {
                    Log.Error($"Error while get token contracts for " +
                        $"address: {address}. " +
                        $"Code: {tokenContractsResult.Error.Code}. " +
                        $"Description: {tokenContractsResult.Error.Description}.");

                    return;
                }

                var contractType = tokenContractsResult.Value
                    .GetContractType(contractAddress);

                var tokenBalancesResult = await bcdApi
                    .GetTokenBalancesAsync(address, contractAddress, cancellationToken)
                    .ConfigureAwait(false);

                if (tokenBalancesResult.HasError)
                {
                    Log.Error("Error while scan tokens balance for " +
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
                    .ToDictionary(tb => TezosConfig.UniqueTokenId(tb.Contract, tb.TokenId, contractType), tb => tb);

                foreach (var localTokenAddress in localTokenAddresses)
                {
                    var tokenId = localTokenAddress.Currency;

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
                    Currency    = TezosConfig.UniqueTokenId(tb.Contract, tb.TokenId, contractType),
                    KeyIndex    = xtzAddress.KeyIndex,
                    HasActivity = true
                });

                localTokenAddresses.AddRange(newTokenAddresses);

                await _tezosAccount.DataRepository
                    .UpsertTezosTokenAddressesAsync(localTokenAddresses)
                    .ConfigureAwait(false);

                // scan transfers

                foreach (var localTokenAddress in localTokenAddresses)
                {
                    var currencyParts = localTokenAddress.Currency.Split(':');

                    var transfersResult = await bcdApi
                        .GetTokenTransfers(
                            address: localTokenAddress.Address,
                            contractAddress: currencyParts[0],
                            tokenId: decimal.Parse(currencyParts[1]),
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (transfersResult.HasError)
                    {
                        Log.Error($"Error while get transfers for " +
                            $"address: {localTokenAddress.Address}, " +
                            $"contract: {currencyParts[0]} and " +
                            $"token id: {currencyParts[1]}. " +
                            $"Code: {transfersResult.Error.Code}. " +
                            $"Description: {transfersResult.Error.Description}.");

                        return;
                    }

                    if (transfersResult?.Value?.Any() ?? false)
                        await _tezosAccount.DataRepository
                            .UpsertTezosTokenTransfersAsync(transfersResult.Value)
                            .ConfigureAwait(false);
                }

            }, cancellationToken);
        }
    }
}