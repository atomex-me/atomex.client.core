using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

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
        private const int DefaultInternalLookAhead = 1;
        private const int DefaultExternalLookAhead = 1;

        protected int InternalLookAhead { get; } = DefaultInternalLookAhead;
        protected int ExternalLookAhead { get; } = DefaultExternalLookAhead;
        private Erc20Config Erc20Config => _account.Currencies.Get<Erc20Config>(_account.Currency);
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
            try
            {
                var updateTimeStamp = DateTime.UtcNow;

                var ethAddresses = await _ethereumAccount
                    .GetAddressesAsync(cancellationToken)
                    .ConfigureAwait(false);

                var walletAddresses = ethAddresses.Select(w => new WalletAddress
                {
                    Address               = w.Address,
                    Currency              = _account.Currency,
                    HasActivity           = false,
                    KeyIndex              = w.KeyIndex,
                    KeyType               = w.KeyType,
                    LastSuccessfullUpdate = DateTime.MinValue,
                    Balance               = 0,
                    UnconfirmedIncome     = 0,
                    UnconfirmedOutcome    = 0,
                    TokenBalance          = null
                });

                var api = GetErc20Api(); 
                var txs = new List<Erc20Transaction>();

                foreach (var walletAddress in walletAddresses)
                {
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

                if (walletAddresses.Any())
                {
                    var _ = await _account
                        .LocalStorage
                        .UpsertAddressesAsync(walletAddresses)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                Log.Debug("[Erc20WalletScanner] ScanAsync canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "[Erc20WalletScanner] ScanAsync error: {@message}", e.Message);
            }
        }

        public Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return UpdateBalanceAsync(address, cancellationToken);
        }

        public async Task UpdateBalanceAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var updateTimeStamp = DateTime.UtcNow;

                var walletAddresses = await _account
                    .GetAddressesAsync(cancellationToken)
                    .ConfigureAwait(false);

                // todo: if skipUsed == true => skip "disabled" wallets

                var api = GetErc20Api();
                var txs = new List<Erc20Transaction>();

                foreach (var walletAddress in walletAddresses)
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

                if (walletAddresses.Any())
                {
                    var _ = await _account
                        .LocalStorage
                        .UpsertAddressesAsync(walletAddresses)
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
                    .GetWalletAddressAsync(_account.Currency, address)
                    .ConfigureAwait(false);

                if (walletAddress == null)
                {
                    Log.Error("[Erc20WalletScanner] UpdateBalanceAsync error. Can't find address {@address} in local db", address);
                    return;
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
                            .GetTransactionByIdAsync<Erc20Transaction>(_account.Currency, tx.Id)
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
                    .UpsertAddressAsync(walletAddress)
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
                    token: Erc20Config.ERC20ContractAddress,
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

            var (lastBlockNumber, lastBlockNumberError) = await api
                .GetRecentBlockHeightAsync()
                .ConfigureAwait(false);

            if (lastBlockNumberError != null)
            {
                Log.Error(
                    "[Erc20WalletScanner] Error while getting last block number with code {@code} and message {@message}",
                    lastBlockNumberError.Value.Code,
                    lastBlockNumberError.Value.Message);

                return lastBlockNumberError;
            }

            if (lastBlockNumber <= 0)
            {
                Log.Error(
                    "[Erc20WalletScanner] Error in block number {@lastBlockNumber}",
                    lastBlockNumber);

                return new Error(Errors.InvalidResponse, "Invalid last block number");
            }

            var (txs, txsError) = await api
                .GetErc20TransactionsAsync(
                    address: walletAddress.Address,
                    tokenContractAddress: Erc20Config.ERC20ContractAddress,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txsError != null)
            {
                Log.Error(
                    "[Erc20WalletScanner] Error while get erc20 transactions",
                    lastBlockNumber);

                return new Error(Errors.InvalidResponse, "Error while get erc20 transactions");
            }

            walletAddress.Balance = balance;
            walletAddress.UnconfirmedIncome = 0;
            walletAddress.UnconfirmedOutcome = 0;
            walletAddress.HasActivity = txs.Transactions.Any();
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
                });
        }

        private List<Erc20Transfer> UnionTransfers(IEnumerable<Erc20Transaction> txs)
        {
            var transfers = new List<Erc20Transfer>(txs.Sum(t => t.Transfers.Count));

            foreach (var tx in txs)
                transfers.AddRange(tx.Transfers);

            transfers.Distinct(new Common.EqualityComparer<Erc20Transfer>(
                (t1, t2) => t1.From.Equals(t2.From) && t1.To.Equals(t2.To) && t1.Value.Equals(t2.Value),
                t => t.From.GetHashCode() ^ t.To.GetHashCode() ^ t.Value.GetHashCode()
            ));

            return transfers;
        }

        private List<Erc20Transfer> UnionTransfers(params Erc20Transaction[] txs) =>
            UnionTransfers(txs.AsEnumerable());
    }
}