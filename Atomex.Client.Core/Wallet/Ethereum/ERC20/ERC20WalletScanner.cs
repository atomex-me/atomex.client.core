using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Ethereum;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;
using Atomex.Blockchain.Ethereum.ERC20;
using Serilog;

namespace Atomex.Wallet.Ethereum
{
    public class ERC20WalletScanner : ICurrencyHdWalletScanner
    {
        private const int DefaultInternalLookAhead = 1;
        private const int DefaultExternalLookAhead = 1;

        protected int InternalLookAhead { get; } = DefaultInternalLookAhead;
        protected int ExternalLookAhead { get; } = DefaultExternalLookAhead;
        private EthereumTokens.ERC20 Currency => Account.Currencies.Get<EthereumTokens.ERC20>(Account.Currency);
        private ERC20Account Account { get; }
        private EthereumAccount EthereumAccount { get; }

        public ERC20WalletScanner(ERC20Account account, EthereumAccount ethereumAccount)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
            EthereumAccount = ethereumAccount ?? throw new ArgumentNullException(nameof(ethereumAccount));
        }

        public async Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            var currency = Currency;

            var scanParams = new[]
            {
                new {Chain = HdKeyStorage.NonHdKeysChain, LookAhead = 0},
                new {Chain = Bip44.Internal, LookAhead = InternalLookAhead},
                new {Chain = Bip44.External, LookAhead = ExternalLookAhead},
            };

            var txs = new List<EthereumTransaction>();

            var api = new EtherScanApi(currency);

            var lastBlockNumberResult = await api
                .GetBlockNumber()
                .ConfigureAwait(false);

            if (lastBlockNumberResult.HasError)
            {
                Log.Error(
                    "Error while getting last block number with code {@code} and description {@description}",
                    lastBlockNumberResult.Error.Code,
                    lastBlockNumberResult.Error.Description);

                return;
            }

            var lastBlockNumber = lastBlockNumberResult.Value;

            if (lastBlockNumber <= 0)
            {
                Log.Error("Error in block number {@lastBlockNumber}", lastBlockNumber);
                return;
            }

            var ethereumAddresses = await ScanEthereumAddressesAsync(lastBlockNumber, cancellationToken)
                .ConfigureAwait(false);

            foreach (var param in scanParams)
            {
                var freeKeysCount = 0;
                var index = 0u;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var walletAddress = await Account
                        .DivideAddressAsync(param.Chain, index, cancellationToken)
                        .ConfigureAwait(false);

                    if (walletAddress == null)
                        break;

                    if (ethereumAddresses.Contains(walletAddress.Address))
                    {
                        index++;
                        continue;
                    }
                        

                    Log.Debug(
                        "Scan transactions for {@name} address {@chain}:{@index}:{@address}",
                        currency.Name,
                        param.Chain,
                        index,
                        walletAddress.Address);

                    var OutEventsResult = await api
                        .GetContractEventsAsync(
                            address: currency.ERC20ContractAddress,
                            fromBlock: currency.SwapContractBlockNumber,
                            toBlock: ulong.MaxValue,
                            topic0: EventSignatureExtractor.GetSignatureHash<ERC20TransferEventDTO>(),
                            topic1: "0x000000000000000000000000" + walletAddress.Address.Substring(2),
                            topic2: null,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (OutEventsResult.HasError)
                    {
                        Log.Error(
                            "Error while scan address transactions for {@address} with code {@code} and description {@description}",
                            walletAddress.Address,
                            OutEventsResult.Error.Code,
                            OutEventsResult.Error.Description);
                        break;
                    }

                    var InEventsResult = await api
                        .GetContractEventsAsync(
                            address: currency.ERC20ContractAddress,
                            fromBlock: currency.SwapContractBlockNumber,
                            toBlock: ulong.MaxValue,
                            topic0: EventSignatureExtractor.GetSignatureHash<ERC20TransferEventDTO>(),
                            topic1: null,
                            topic2: "0x000000000000000000000000" + walletAddress.Address.Substring(2),
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (InEventsResult.HasError)
                    {
                        Log.Error(
                            "Error while scan address transactions for {@address} with code {@code} and description {@description}",
                            walletAddress.Address,
                            InEventsResult.Error.Code,
                            InEventsResult.Error.Description);
                        break;
                    }

                    var events = OutEventsResult.Value?.ToList().Concat(InEventsResult.Value?.ToList());

                    if (events == null || !events.Any())
                    {
                        freeKeysCount++;

                        if (freeKeysCount >= param.LookAhead)
                        {
                            Log.Debug("{@lookAhead} free keys found. Chain scan completed", param.LookAhead);
                            break;
                        }
                    }
                    else // address has activity
                    {
                        freeKeysCount = 0;

                        foreach (var ev in events)
                        {
                            var transferEvent = ev.ParseERC20TransferEvent();

                            var tx = ev.TransformTransferEvent(currency, lastBlockNumber);

                            txs.Add(tx);
                        }
                    }

                    index++;
                }
            }

            if (txs.Any())
                await UpsertTransactionsAsync(txs)
                    .ConfigureAwait(false);

            await Account
                .UpdateBalanceAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<IEnumerable<string>> ScanEthereumAddressesAsync(
             long blockNumber,
             CancellationToken cancellationToken = default)
        {
            var ethereumAddresses = await EthereumAccount
                .GetUnspentAddressesAsync(cancellationToken)
                .ConfigureAwait(false);

            var currency = Currency;

            var txs = new List<EthereumTransaction>();

            var api = new EtherScanApi(currency);

            foreach (var ethereumAddress in ethereumAddresses)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Log.Debug(
                    "Scan transactions for {@name} address {@address}",
                    currency.Name,
                    ethereumAddress.Address);

                    var OutEventsResult = await api
                    .GetContractEventsAsync(
                        address: currency.ERC20ContractAddress,
                        fromBlock: currency.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<ERC20TransferEventDTO>(),
                        topic1: "0x000000000000000000000000" + ethereumAddress.Address.Substring(2),
                        topic2: null,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (OutEventsResult.HasError)
                {
                    Log.Error(
                        "Error while scan address transactions for {@address} with code {@code} and description {@description}",
                        ethereumAddress.Address,
                        OutEventsResult.Error.Code,
                        OutEventsResult.Error.Description);
                    break;
                }

                var InEventsResult = await api
                    .GetContractEventsAsync(
                        address: currency.ERC20ContractAddress,
                        fromBlock: currency.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<ERC20TransferEventDTO>(),
                        topic1: null,
                        topic2: "0x000000000000000000000000" + ethereumAddress.Address.Substring(2),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (InEventsResult.HasError)
                {
                    Log.Error(
                        "Error while scan address transactions for {@address} with code {@code} and description {@description}",
                        ethereumAddress.Address,
                        InEventsResult.Error.Code,
                        InEventsResult.Error.Description);
                    break;
                }

                var events = OutEventsResult.Value?
                    .ToList()
                    .Concat(InEventsResult.Value?.ToList());

                if (events.Any())
                {
                    foreach (var ev in events)
                    {
                        var transferEvent = ev.ParseERC20TransferEvent();

                        var tx = ev.TransformTransferEvent(currency, blockNumber);

                        txs.Add(tx);
                    }
                }
            }

            if (txs.Any())
                await UpsertTransactionsAsync(txs)
                    .ConfigureAwait(false);

            return ethereumAddresses.Select(a => a.Address);
        }

        public async Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var currency = Currency;

            Log.Debug("Scan transactions for {@currency} address {@address}",
                Currency.Name,
                address);

            var txs = new List<EthereumTransaction>();
            var api = new EtherScanApi(currency);

            var lastBlockNumberResult = await api
                .GetBlockNumber()
                .ConfigureAwait(false);

            if (lastBlockNumberResult.HasError)
            {
                Log.Error(
                    "Error while getting last block number with code {@code} and description {@description}",
                    lastBlockNumberResult.Error.Code,
                    lastBlockNumberResult.Error.Description);
                return;
            }

            var lastBlockNumber = lastBlockNumberResult.Value;

            if (lastBlockNumber <= 0)
            {
                Log.Error(
                    "Error in block number {@lastBlockNumber}",
                    lastBlockNumber);
                return;
            }

            var OutEventsResult = await api
                .GetContractEventsAsync(
                    address: currency.ERC20ContractAddress,
                    fromBlock: currency.SwapContractBlockNumber,
                    toBlock: ulong.MaxValue,
                    topic0: EventSignatureExtractor.GetSignatureHash<ERC20TransferEventDTO>(),
                    topic1: "0x000000000000000000000000" + address.Substring(2),
                    topic2: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (OutEventsResult.HasError)
            {
                Log.Error(
                    "Error while scan address transactions for {@address} with code {@code} and description {@description}",
                    address,
                    OutEventsResult.Error.Code,
                    OutEventsResult.Error.Description);
                return;
            }

            var InEventsResult = await api
                .GetContractEventsAsync(
                    address: currency.ERC20ContractAddress,
                    fromBlock: currency.SwapContractBlockNumber,
                    toBlock: ulong.MaxValue,
                    topic0: EventSignatureExtractor.GetSignatureHash<ERC20TransferEventDTO>(),
                    topic1: null,
                    topic2: "0x000000000000000000000000" + address.Substring(2),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (InEventsResult.HasError)
            {
                Log.Error(
                    "Error while scan address transactions for {@address} with code {@code} and description {@description}",
                    address,
                    InEventsResult.Error.Code,
                    InEventsResult.Error.Description);
                return;
            }

            var events = OutEventsResult.Value?.ToList().Concat(InEventsResult.Value?.ToList());

            if (events == null || !events.Any()) // address without activity
                return;

            foreach (var ev in events)
            {
                var transferEvent = ev.ParseERC20TransferEvent();

                var tx = ev.TransformTransferEvent(currency, lastBlockNumber);

                if (tx.From == address || tx.To == address)
                    txs.Add(tx);
            }

            if (txs.Any())
                await UpsertTransactionsAsync(txs)
                    .ConfigureAwait(false);

            await Account
                .UpdateBalanceAsync(address: address, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task UpsertTransactionsAsync(IEnumerable<EthereumTransaction> transactions)
        {
            foreach (var tx in transactions)
            {
                await Account
                    .UpsertTransactionAsync(
                        tx: tx,
                        updateBalance: false,
                        notifyIfUnconfirmed: false,
                        notifyIfBalanceUpdated: false)
                    .ConfigureAwait(false);
            }
        }
    }
}