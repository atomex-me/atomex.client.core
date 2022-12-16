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

        private EtherScanApi GetErc20Api()
        {
            var apiSettings = new EtherScanSettings // TODO: use config
            {
                BaseUri = Erc20Config.BlockchainApiBaseUri
            };

            return new EtherScanApi(apiSettings);
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
                            Log.Error("[Erc20WalletScanner] UpdateBalanceAsync error while scan {@address}",
                                walletAddress.Address);

                            return;
                        }

                        txs.AddRange(addressTxs);
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
                            Log.Error("[Erc20WalletScanner] UpdateBalanceAsync error while scan {@address}",
                                walletAddress.Address);

                            return;
                        }

                        txs.AddRange(addressTxs);
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

                    var (addressTxs, error) = await UpdateAddressAsync(
                            walletAddress,
                            api: null,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (error != null)
                    {
                        Log.Error("[Erc20WalletScanner] UpdateBalanceAsync error while scan {@address}",
                            address);

                        return;
                    }

                    if (addressTxs.Any())
                    {
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

            }, cancellationToken);
        }

        private async Task<Result<IEnumerable<Erc20Transaction>>> UpdateAddressAsync(
            WalletAddress walletAddress,
            EtherScanApi api = null,
            CancellationToken cancellationToken = default)
        {
            var updateTimeStamp = DateTime.UtcNow;

            if (api == null)
                api = GetErc20Api();

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

            walletAddress.Balance = Erc20Config.TokenDigitsToTokens(balance);
            walletAddress.UnconfirmedIncome = 0;
            walletAddress.UnconfirmedOutcome = 0;
            walletAddress.HasActivity = txs.Transactions.Any();
            walletAddress.LastSuccessfullUpdate = updateTimeStamp;

            return new Result<IEnumerable<Erc20Transaction>> { Value = txs.Transactions };
        }

        private async Task<Result<IEnumerable<ContractEvent>>> GetErc20EventsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var currency = Erc20Config;
            var api = GetErc20Api();

            var (approveEvents, approveEventsError) = await api
                .GetContractEventsAsync(
                    address: currency.ERC20ContractAddress,
                    fromBlock: currency.SwapContractBlockNumber,
                    toBlock: ulong.MaxValue,
                    topic0: EventSignatureExtractor.GetSignatureHash<Erc20ApprovalEventDTO>(),
                    topic1: "0x000000000000000000000000" + address[2..],
                    topic2: "0x000000000000000000000000" + currency.SwapContractAddress[2..],
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (approveEventsError != null)
            {
                Log.Error(
                    "[Erc20WalletScanner] Error while scan address transactions for {@address} with code {@code} and message {@message}",
                    address,
                    approveEventsError.Value.Code,
                    approveEventsError.Value.Message);

                return approveEventsError;
            }

            var (outEvents, outEventsError) = await api
                .GetContractEventsAsync(
                    address: currency.ERC20ContractAddress,
                    fromBlock: currency.SwapContractBlockNumber,
                    toBlock: ulong.MaxValue,
                    topic0: EventSignatureExtractor.GetSignatureHash<Erc20TransferEventDTO>(),
                    topic1: "0x000000000000000000000000" + address[2..],
                    topic2: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (outEventsError != null)
            {
                Log.Error(
                    "[Erc20WalletScanner] Error while scan address transactions for {@address} with code {@code} and message {@message}",
                    address,
                    outEventsError.Value.Code,
                    outEventsError.Value.Message);

                return outEventsError;
            }

            var (inEvents, inEventsError) = await api
                .GetContractEventsAsync(
                    address: currency.ERC20ContractAddress,
                    fromBlock: currency.SwapContractBlockNumber,
                    toBlock: ulong.MaxValue,
                    topic0: EventSignatureExtractor.GetSignatureHash<Erc20TransferEventDTO>(),
                    topic1: null,
                    topic2: "0x000000000000000000000000" + address[2..],
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (inEventsError != null)
            {
                Log.Error(
                    "[Erc20WalletScanner] Error while scan address transactions for {@address} with code {@code} and message {@message}",
                    address,
                    inEventsError.Value.Code,
                    inEventsError.Value.Message);

                return inEventsError;
            }

            return new Result<IEnumerable<ContractEvent>>
            {
                Value = approveEvents
                    .Concat(outEvents)
                    .Concat(inEvents)
            };
        }
    }
}