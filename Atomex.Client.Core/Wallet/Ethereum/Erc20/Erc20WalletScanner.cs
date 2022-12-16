using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Erc20;
using Atomex.Blockchain.Ethereum.Erc20.Dto;
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

        public Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
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

                    // todo: if skipUsed == true => skip "disabled" wallets

                    var api = new EtherScanApi(Erc20Config.Name, Erc20Config.BlockchainApiBaseUri);
                    var txs = new List<EthereumTransaction>();

                    foreach (var walletAddress in walletAddresses)
                    {
                        var txsResult = await UpdateAddressAsync(
                                walletAddress,
                                api: api,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (txsResult.HasError)
                        {
                            Log.Error("[Erc20WalletScanner] UpdateBalanceAsync error while scan {@address}", walletAddress.Address);
                            return;
                        }

                        txs.AddRange(txsResult.Value);
                    }

                    if (txs.Any())
                    {
                        var _ = await _account
                            .LocalStorage
                            .UpsertTransactionsAsync(
                                txs: txs,
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

            }, cancellationToken);
        }

        public Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return UpdateBalanceAsync(address, cancellationToken);
        }

        public Task UpdateBalanceAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var updateTimeStamp = DateTime.UtcNow;

                    var walletAddresses = await _account
                        .GetAddressesAsync(cancellationToken)
                        .ConfigureAwait(false);

                    // todo: if skipUsed == true => skip "disabled" wallets

                    var api = new EtherScanApi(Erc20Config.Name, Erc20Config.BlockchainApiBaseUri);
                    var txs = new List<EthereumTransaction>();

                    foreach (var walletAddress in walletAddresses)
                    {
                        var txsResult = await UpdateAddressAsync(
                                walletAddress,
                                api: api,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (txsResult.HasError)
                        {
                            Log.Error("[Erc20WalletScanner] UpdateBalanceAsync error while scan {@address}", walletAddress.Address);
                            return;
                        }

                        txs.AddRange(txsResult.Value);
                    }

                    if (txs.Any())
                    {
                        var _ = await _account
                            .LocalStorage
                            .UpsertTransactionsAsync(
                                txs: txs,
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

            }, cancellationToken);
        }

        public Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
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

                    var txsResult = await UpdateAddressAsync(
                            walletAddress,
                            api: null,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (txsResult.HasError)
                    {
                        Log.Error("[Erc20WalletScanner] UpdateBalanceAsync error while scan {@address}", address);
                        return;
                    }

                    if (txsResult.Value.Any())
                    {
                        await _account
                            .LocalStorage
                            .UpsertTransactionsAsync(
                                txs: txsResult.Value,
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

            }, cancellationToken);
        }

        private async Task<Result<IEnumerable<EthereumTransaction>>> UpdateAddressAsync(
            WalletAddress walletAddress,
            EtherScanApi api = null,
            CancellationToken cancellationToken = default)
        {
            var updateTimeStamp = DateTime.UtcNow;

            if (api == null)
                api = new EtherScanApi(Erc20Config.Name, Erc20Config.BlockchainApiBaseUri);

            var balanceResult = await api
                .TryGetErc20BalanceAsync(
                    address: walletAddress.Address,
                    contractAddress: Erc20Config.ERC20ContractAddress,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (balanceResult.HasError)
            {
                Log.Error("[Erc20WalletScanner] Error while getting balance for {@address} with code {@code} and description {@description}",
                    walletAddress.Address,
                    balanceResult.Error.Code,
                    balanceResult.Error.Description);

                return balanceResult.Error; // todo: may be return?
            }

            var txs = new List<EthereumTransaction>();

            var lastBlockNumberResult = await api
                .GetBlockNumber()
                .ConfigureAwait(false);

            if (lastBlockNumberResult.HasError)
            {
                Log.Error(
                    "[Erc20WalletScanner] Error while getting last block number with code {@code} and description {@description}",
                    lastBlockNumberResult.Error.Code,
                    lastBlockNumberResult.Error.Description);

                return lastBlockNumberResult.Error;
            }

            var lastBlockNumber = lastBlockNumberResult.Value;

            if (lastBlockNumber <= 0)
            {
                Log.Error(
                    "[Erc20WalletScanner] Error in block number {@lastBlockNumber}",
                    lastBlockNumber);

                return new Error(Errors.InvalidResponse, "Invalid last block number");
            }

            var events = await GetErc20EventsAsync(walletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            if (events != null && events.Any())
            {
                foreach (var ev in events)
                {
                    var tx = new EthereumTransaction();

                    if (ev.IsErc20ApprovalEvent())
                        tx = ev.TransformApprovalEvent(Erc20Config, lastBlockNumber);
                    else if (ev.IsErc20TransferEvent())
                        tx = ev.TransformTransferEvent(walletAddress.Address, Erc20Config, lastBlockNumber);

                    if (tx != null)
                        txs.Add(tx);
                }
            }

            walletAddress.Balance = Erc20Config.TokenDigitsToTokens(balanceResult.Value);
            walletAddress.UnconfirmedIncome = 0;
            walletAddress.UnconfirmedOutcome = 0;
            walletAddress.HasActivity = txs.Any();
            walletAddress.LastSuccessfullUpdate = updateTimeStamp;

            return new Result<IEnumerable<EthereumTransaction>>(txs);
        }

        private async Task<List<ContractEvent>> GetErc20EventsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var currency = Erc20Config;
            var api = new EtherScanApi(currency.Name, currency.BlockchainApiBaseUri);

            var approveEventsResult = await api
                .GetContractEventsAsync(
                    address: currency.ERC20ContractAddress,
                    fromBlock: currency.SwapContractBlockNumber,
                    toBlock: ulong.MaxValue,
                    topic0: EventSignatureExtractor.GetSignatureHash<Erc20ApprovalEventDTO>(),
                    topic1: "0x000000000000000000000000" + address[2..],
                    topic2: "0x000000000000000000000000" + currency.SwapContractAddress[2..],
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (approveEventsResult.HasError)
            {
                Log.Error(
                    "[Erc20WalletScanner] Error while scan address transactions for {@address} with code {@code} and description {@description}",
                    address,
                    approveEventsResult.Error.Code,
                    approveEventsResult.Error.Description);

                return null;
            }

            var outEventsResult = await api
                .GetContractEventsAsync(
                    address: currency.ERC20ContractAddress,
                    fromBlock: currency.SwapContractBlockNumber,
                    toBlock: ulong.MaxValue,
                    topic0: EventSignatureExtractor.GetSignatureHash<Erc20TransferEventDTO>(),
                    topic1: "0x000000000000000000000000" + address[2..],
                    topic2: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (outEventsResult.HasError)
            {
                Log.Error(
                    "[Erc20WalletScanner] Error while scan address transactions for {@address} with code {@code} and description {@description}",
                    address,
                    outEventsResult.Error.Code,
                    outEventsResult.Error.Description);

                return null;
            }

            var inEventsResult = await api
                .GetContractEventsAsync(
                    address: currency.ERC20ContractAddress,
                    fromBlock: currency.SwapContractBlockNumber,
                    toBlock: ulong.MaxValue,
                    topic0: EventSignatureExtractor.GetSignatureHash<Erc20TransferEventDTO>(),
                    topic1: null,
                    topic2: "0x000000000000000000000000" + address[2..],
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (inEventsResult.HasError)
            {
                Log.Error(
                    "[Erc20WalletScanner] Error while scan address transactions for {@address} with code {@code} and description {@description}",
                    address,
                    inEventsResult.Error.Code,
                    inEventsResult.Error.Description);

                return null;
            }

            var events = approveEventsResult.Value?
                .Concat(outEventsResult.Value?.Concat(inEventsResult.Value))
                .ToList();

            if (events == null || !events.Any()) // address without activity
                return null;

            return events;
        }
    }
}