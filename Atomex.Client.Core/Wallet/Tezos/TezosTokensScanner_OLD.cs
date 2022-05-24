using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Core;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet.Tezos
{
    public class TezosTokensScanner_OLD : ICurrencyHdWalletScanner_OLD
    {
        private readonly TezosAccount _tezosAccount;

        public TezosTokensScanner_OLD(TezosAccount tezosAccount)
        {
            _tezosAccount = tezosAccount ?? throw new ArgumentNullException(nameof(tezosAccount));
        }

        public Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                // all tezos addresses
                var xtzAddresses = await _tezosAccount.DataRepository
                    .GetAddressesAsync(TezosConfig_OLD.Xtz)
                    .ConfigureAwait(false);

                if (xtzAddresses.Count() <= 1)
                {
                    // firstly scan xtz
                    await new TezosWalletScanner(_tezosAccount)
                        .ScanAsync(cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    xtzAddresses = await _tezosAccount.DataRepository
                        .GetAddressesAsync(TezosConfig_OLD.Xtz)
                        .ConfigureAwait(false);
                }

                // addresses from local db
                var localAddresses = await _tezosAccount.DataRepository
                    .GetTezosTokenAddressesAsync()
                    .ConfigureAwait(false);

                var addresses = localAddresses
                    .Select(a => a.Address)
                    .ToList();

                if (xtzAddresses.Any())
                    addresses.AddRange(xtzAddresses.Select(a => a.Address));

                // remove duplicates
                addresses = addresses
                    .Distinct()
                    .ToList();

                foreach (var address in addresses)
                {
                    await ScanAsync(address, cancellationToken)
                        .ConfigureAwait(false);
                }

            }, cancellationToken);
        }

        public Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var tzktApi = new TzktApi_OLD(_tezosAccount.Config);

                var tokenContractsResult = await tzktApi
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

                // upsert contracts
                if (tokenContractsResult.Value.Any())
                    await _tezosAccount.DataRepository
                        .UpsertTezosTokenContractsAsync(tokenContractsResult.Value)
                        .ConfigureAwait(false);

                // contracts from local db
                var contracts = (await _tezosAccount.DataRepository
                    .GetTezosTokenAddressesAsync(address)
                    .ConfigureAwait(false))
                    .Select(a => a.TokenBalance.Contract)
                    .ToList();

                // add contracts from network
                if (tokenContractsResult.Value.Any())
                    contracts.AddRange(tokenContractsResult.Value.Select(x => x.Address));

                contracts = contracts
                    .Distinct()
                    .ToList();

                // scan by address and contract
                foreach (var contractAddress in contracts)
                {
                    var contractWithMetadata = tokenContractsResult.Value.Find(x => x.Address == contractAddress);
                    await ScanContractAsync(
                            address,
                            contractAddress,
                            contractWithMetadata,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

            }, cancellationToken);
        }

        public Task ScanContractAsync(
            string contractAddress,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                // addresses from local db
                var localAddresses = await _tezosAccount.DataRepository
                    .GetTezosTokenAddressesByContractAsync(contractAddress)
                    .ConfigureAwait(false);

                // all tezos addresses
                var xtzAddresses = await _tezosAccount.DataRepository
                    .GetAddressesAsync(TezosConfig_OLD.Xtz)
                    .ConfigureAwait(false);

                if (xtzAddresses.Count() <= 1)
                {
                    // firstly scan xtz
                    await new TezosWalletScanner(_tezosAccount)
                        .ScanAsync(cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    xtzAddresses = await _tezosAccount.DataRepository
                        .GetAddressesAsync(TezosConfig_OLD.Xtz)
                        .ConfigureAwait(false);
                }

                var addresses = localAddresses
                    .Select(a => a.Address)
                    .ToList();

                if (xtzAddresses.Any())
                    addresses.AddRange(xtzAddresses.Select(a => a.Address));

                // remove duplicates
                addresses = addresses
                    .Distinct()
                    .ToList();

                foreach (var address in addresses)
                {
                    await ScanContractAsync(
                            address,
                            contractAddress,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

            }, cancellationToken);
        }

        public Task ScanContractAsync(
            string address,
            string contractAddress,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var tezosConfig = _tezosAccount.Config;

                var tzktApi = new TzktApi_OLD(tezosConfig);

                var tokenContractsResult = await tzktApi
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

                // upsert contracts
                if (tokenContractsResult.Value.Any())
                    await _tezosAccount.DataRepository
                        .UpsertTezosTokenContractsAsync(tokenContractsResult.Value)
                        .ConfigureAwait(false);

                var contractWithMetadata = tokenContractsResult.Value.Find(x => x.Address == contractAddress);
                await ScanContractAsync(
                        address,
                        contractAddress,
                        contractWithMetadata,
                        cancellationToken)
                    .ConfigureAwait(false);

            }, cancellationToken);
        }
    
        private Task ScanContractAsync(
            string address,
            string contractAddress,
            TokenContract contractWithMetadata,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var tezosConfig = _tezosAccount.Config;

                var tzktApi = new TzktApi_OLD(tezosConfig);

                var contractType = contractWithMetadata?.Type ?? "FA2";

                var tokenBalancesResult = await tzktApi
                    .GetTokenBalancesAsync(
                        address: address,
                        contractAddress: contractAddress,
                        cancellationToken: cancellationToken)
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
                    .GroupBy(tb => UniqueTokenId(contractType, tb.Contract, tb.TokenId))
                    .ToDictionary(
                        g => g.Key,
                        g => g.First());

                foreach (var localAddress in localTokenAddresses)
                {
                    var uniqueTokenId = UniqueTokenId(
                        localAddress.Currency,
                        localAddress.TokenBalance.Contract,
                        localAddress.TokenBalance.TokenId);

                    if (tokenBalanceDict.TryGetValue(uniqueTokenId, out var tb))
                    {
                        localAddress.Balance = tb.GetTokenBalance();
                        localAddress.TokenBalance = tb;

                        tokenBalanceDict.Remove(uniqueTokenId);
                    }
                    else
                    {
                        localAddress.Balance = 0;
                        localAddress.TokenBalance.Balance = "0";
                    }
                }

                var xtzAddress = await _tezosAccount
                    .GetAddressAsync(address, cancellationToken)
                    .ConfigureAwait(false);

                var newTokenAddresses = tokenBalanceDict.Values
                    .Select(tb => new WalletAddress_OLD
                    {
                        Address      = address,
                        Balance      = tb.GetTokenBalance(),
                        Currency     = contractType,
                        KeyIndex     = xtzAddress.KeyIndex,
                        KeyType      = xtzAddress.KeyType,
                        HasActivity  = true,
                        TokenBalance = tb
                    });

                localTokenAddresses.AddRange(newTokenAddresses);

                if (localTokenAddresses.Any())
                    await _tezosAccount.DataRepository
                        .UpsertTezosTokenAddressesAsync(localTokenAddresses)
                        .ConfigureAwait(false);

                // scan transfers

                foreach (var localAddress in localTokenAddresses)
                {
                    var transfersResult = await tzktApi
                        .GetTokenTransfersAsync(
                            address: localAddress.Address,
                            contractAddress: localAddress.TokenBalance.Contract,
                            tokenId: localAddress.TokenBalance.TokenId,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (transfersResult.HasError)
                    {
                        Log.Error($"Error while get transfers for " +
                            $"address: {localAddress.Address}, " +
                            $"contract: {localAddress.TokenBalance.Contract} and " +
                            $"token id: {localAddress.TokenBalance.TokenId}. " +
                            $"Code: {transfersResult.Error.Code}. " +
                            $"Description: {transfersResult.Error.Description}.");

                        return;
                    }

                    // todo: fix 'self' transfers
                    //transfersResult.Value.ForEach(t =>
                    //{
                    foreach (var t in transfersResult.Value)
                    {
                        t.Currency = contractType;

                        if (localAddress.Address == t.From)
                        {
                            t.Type |= BlockchainTransactionType.Output;
                        }
                        else
                        {
                            var fromAddress = await _tezosAccount
                                .GetAddressAsync(t.From, cancellationToken)
                                .ConfigureAwait(false);

                            if (fromAddress != null)
                                t.Type |= BlockchainTransactionType.Output;
                        }

                        if (localAddress.Address == t.To)
                        {
                            t.Type |= BlockchainTransactionType.Input;
                        }
                        else
                        {
                            var toAddress = await _tezosAccount
                                .GetAddressAsync(t.To, cancellationToken)
                                .ConfigureAwait(false);

                            if (toAddress != null)
                                t.Type |= BlockchainTransactionType.Input;
                        }
                    }

                    if (transfersResult?.Value?.Any() ?? false)
                        await _tezosAccount.DataRepository
                            .UpsertTezosTokenTransfersAsync(transfersResult.Value)
                            .ConfigureAwait(false);
                }

            }, cancellationToken);
        }

        private static string UniqueTokenId(string type, string contract, decimal tokenId) =>
            $"{type}:{contract}:{tokenId}";
    }
}