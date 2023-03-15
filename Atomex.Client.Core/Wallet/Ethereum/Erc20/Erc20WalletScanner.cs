using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Erc20;
using Atomex.Blockchain.Ethereum.EtherScan;
using Atomex.Common;
using Atomex.Core;
using Atomex.EthereumTokens;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet.Ethereum
{
    public class Erc20WalletScanner : ICurrencyWalletScanner
    {
        private Erc20Config Erc20Config => _account.Erc20Config;
        private readonly Erc20Account _account;
        private readonly EthereumAccount _ethereumAccount;

        public Erc20WalletScanner(Erc20Account account, EthereumAccount ethereumAccount)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _ethereumAccount = ethereumAccount ?? throw new ArgumentNullException(nameof(ethereumAccount));
        }

        private EtherScanApi GetErc20Api()
        {
            var apiSettings = new EtherScanSettings // TODO: use config
            {
                BaseUri = Erc20Config.BlockchainApiBaseUri
            };

            return new EtherScanApi(apiSettings);
        }

        public async Task ScanAsync(
            CancellationToken cancellationToken = default)
        {
            var ethAddresses = await _ethereumAccount
                .GetAddressesAsync(cancellationToken)
                .ConfigureAwait(false);

            if (ethAddresses.Count() <= 1)
            {
                await new EthereumWalletScanner(_ethereumAccount)
                    .ScanAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            await UpdateBalanceAsync(skipUsed: false, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task UpdateBalanceAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var updateTimeStamp = DateTime.UtcNow;

                // all tokens addresses
                var tokenLocalAddresses = (await _account
                    .LocalStorage
                    .GetAddressesAsync(
                        currency: EthereumHelper.Erc20,
                        tokenContract: Erc20Config.TokenContractAddress,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
                    .ToList();

                // all ethereum addresses without tokens
                var emptyAddresses = (await _ethereumAccount
                    .GetAddressesAsync(cancellationToken)
                    .ConfigureAwait(false))
                    .Where(w => !tokenLocalAddresses.Any(ta => ta.Address == w.Address))
                    .Select(w => new WalletAddress
                    {
                        Address               = w.Address,
                        Currency              = EthereumHelper.Erc20,
                        HasActivity           = false,
                        KeyPath               = w.KeyPath,
                        KeyIndex              = w.KeyIndex,
                        KeyType               = w.KeyType,
                        LastSuccessfullUpdate = DateTime.MinValue,
                        Balance               = 0,
                        UnconfirmedIncome     = 0,
                        UnconfirmedOutcome    = 0,
                        TokenBalance = new TokenBalance
                        {
                            Address       = w.Address,
                            Standard      = EthereumHelper.Erc20,
                            Contract      = Erc20Config.TokenContractAddress,
                            ContractAlias = Erc20Config.Name,
                            Decimals      = Erc20Config.Decimals,
                            Name          = Erc20Config.Name,
                            Description   = Erc20Config.Description,
                            Symbol        = Erc20Config.Name,
                            Balance       = "0"
                        }
                    })
                    .ToList();

                tokenLocalAddresses.AddRange(emptyAddresses);

                var api = GetErc20Api();
                var txs = new List<Erc20Transaction>();

                foreach (var walletAddress in tokenLocalAddresses)
                {
                    if (skipUsed && walletAddress.IsDisabled)
                        continue;

                    var (addressTxs, error) = await UpdateAddressAsync(
                            walletAddress,
                            api: api,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (error != null)
                    {
                        Log.Error("[Erc20WalletScanner] UpdateBalanceAsync error while scan {@address}", walletAddress.Address);
                        return;
                    }

                    txs.AddRange(addressTxs);
                }

                if (txs.Any())
                {
                    var uniqueTxs = DistinctTransactions(txs);

                    var _ = await _account
                        .LocalStorage
                        .UpsertTransactionsAsync(
                            txs: uniqueTxs,
                            notifyIfNewOrChanged: true,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                if (tokenLocalAddresses.Any())
                {
                    var _ = await _account
                        .LocalStorage
                        .UpsertAddressesAsync(
                            walletAddresses: tokenLocalAddresses,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                Log.Debug("[Erc20WalletScanner] UpdateBalanceAsync canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "[Erc20WalletScanner] UpdateBalanceAsync error: {@message}", e.Message);
            }
        }

        public async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("[Erc20WalletScanner] UpdateBalanceAsync for address {@address}", address);

                var walletAddress = await _account
                    .LocalStorage
                    .GetAddressAsync(
                        currency: EthereumHelper.Erc20,
                        tokenContract: Erc20Config.TokenContractAddress,
                        tokenId: 0,
                        address: address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (walletAddress == null)
                {
                    var ethAddress = await _account
                        .LocalStorage
                        .GetAddressAsync(
                            currency: EthereumHelper.Eth,
                            address: address,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (ethAddress == null)
                    {
                        Log.Error("[Erc20WalletScanner] UpdateBalanceAsync error. Can't find address {@address} in local db", address);
                        return;
                    }

                    walletAddress = new WalletAddress
                    {
                        Address               = ethAddress.Address,
                        Currency              = EthereumHelper.Erc20,
                        HasActivity           = false,
                        KeyPath               = ethAddress.KeyPath,
                        KeyIndex              = ethAddress.KeyIndex,
                        KeyType               = ethAddress.KeyType,
                        LastSuccessfullUpdate = DateTime.MinValue,
                        Balance               = 0,
                        UnconfirmedIncome     = 0,
                        UnconfirmedOutcome    = 0,
                        TokenBalance = new TokenBalance
                        {
                            Address       = ethAddress.Address,
                            Standard      = EthereumHelper.Erc20,
                            Contract      = Erc20Config.TokenContractAddress,
                            ContractAlias = Erc20Config.Name,
                            Decimals      = Erc20Config.Decimals,
                            Name          = Erc20Config.Name,
                            Description   = Erc20Config.Description,
                            Symbol        = Erc20Config.Name,
                            Balance       = "0"
                        }
                    };
                }

                var (addressTxs, error) = await UpdateAddressAsync(
                        walletAddress,
                        api: null,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                {
                    Log.Error("[Erc20WalletScanner] UpdateBalanceAsync error while scan {@address}", address);
                    return;
                }

                if (addressTxs.Any())
                {
                    foreach (var tx in addressTxs)
                    {
                        var existsTx = await _account
                            .LocalStorage
                            .GetTransactionByIdAsync<Erc20Transaction>(
                                currency: EthereumHelper.Erc20,
                                txId: tx.Id,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (existsTx != null)
                            tx.Transfers = UnionTransfers(tx, existsTx);
                    }

                    await _account
                        .LocalStorage
                        .UpsertTransactionsAsync(
                            txs: addressTxs,
                            notifyIfNewOrChanged: true,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                var _ = await _account
                    .LocalStorage
                    .UpsertAddressAsync(walletAddress, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("[Erc20WalletScanner] UpdateBalanceAsync canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "[Erc20WalletScanner] UpdateBalanceAsync error: {@message}", e.Message);
            }
        }

        private async Task<Result<IEnumerable<Erc20Transaction>>> UpdateAddressAsync(
            WalletAddress walletAddress,
            EtherScanApi api = null,
            CancellationToken cancellationToken = default)
        {
            var updateTimeStamp = DateTime.UtcNow;

            api ??= GetErc20Api();

            var (balance, error) = await api
                .GetErc20BalanceAsync(
                    address: walletAddress.Address,
                    tokenContractAddress: Erc20Config.TokenContractAddress,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                Log.Error("[Erc20WalletScanner] Error while getting balance for {@address} with code {@code} and message {@message}",
                    walletAddress.Address,
                    error.Value.Code,
                    error.Value.Message);

                return error; // todo: may be return?
            }

            var fromBlock = 0UL;

            if (walletAddress.LastSuccessfullUpdate != DateTime.MinValue)
            {
                var (blockNumber, blockNumberError) = await api
                    .GetBlockNumberAsync(
                        timeStamp: walletAddress.LastSuccessfullUpdate,
                        blockClosest: ClosestBlock.Before,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (blockNumberError != null)
                {
                    Log.Error(
                        "[Erc20WalletScanner] Error while getting block number with code {@code} and message {@message}",
                        blockNumberError.Value.Code,
                        blockNumberError.Value.Message);

                    return blockNumberError;
                }

                fromBlock = blockNumber;
            }

            var (txs, txsError) = await api
                .GetErc20TransactionsAsync(
                    address: walletAddress.Address,
                    tokenContractAddress: Erc20Config.TokenContractAddress,
                    fromBlock: fromBlock,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txsError != null)
            {
                Log.Error("[Erc20WalletScanner] Error while get erc20 transactions");

                return new Error(Errors.InvalidResponse, "Error while get erc20 transactions");
            }

            walletAddress.Balance = balance;
            walletAddress.UnconfirmedIncome = 0;
            walletAddress.UnconfirmedOutcome = 0;
            walletAddress.HasActivity = txs.Transactions.Any();
            walletAddress.TokenBalance.Balance = balance.ToString();
            walletAddress.LastSuccessfullUpdate = updateTimeStamp;

            return new Result<IEnumerable<Erc20Transaction>> { Value = txs.Transactions };
        }

        private IEnumerable<Erc20Transaction> DistinctTransactions(IEnumerable<Erc20Transaction> transactions)
        {
            // todo: try to implement using OrderedDictionary
            return transactions
                .GroupBy(t => t.Id)
                .Select(g =>
                {
                    var firstTx = g.First();

                    if (g.Count() == 1)
                        return firstTx;

                    firstTx.Transfers = UnionTransfers(g);

                    return firstTx;
                })
                .ToList();
        }

        private List<Erc20Transfer> UnionTransfers(IEnumerable<Erc20Transaction> txs)
        {
            var transfers = new List<Erc20Transfer>(txs.Sum(t => t.Transfers.Count));

            foreach (var tx in txs)
                transfers.AddRange(tx.Transfers);

            transfers = transfers
                .Distinct(new Common.EqualityComparer<Erc20Transfer>(
                    (t1, t2) => t1.From.Equals(t2.From) && t1.To.Equals(t2.To) && t1.Value.Equals(t2.Value),
                    t => t.From.GetHashCode() ^ t.To.GetHashCode() ^ t.Value.GetHashCode()
                ))
                .ToList();

            return transfers;
        }

        private List<Erc20Transfer> UnionTransfers(params Erc20Transaction[] txs) =>
            UnionTransfers(txs.AsEnumerable());
    }
}