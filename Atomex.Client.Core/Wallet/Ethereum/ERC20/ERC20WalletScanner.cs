using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.ERC20;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;
using static Atomex.Blockchain.Ethereum.EtherScanApi;

namespace Atomex.Wallet.Ethereum
{
    public class Erc20WalletScanner : ICurrencyHdWalletScanner
    {
        private const int DefaultInternalLookAhead = 1;
        private const int DefaultExternalLookAhead = 1;

        protected int InternalLookAhead { get; } = DefaultInternalLookAhead;
        protected int ExternalLookAhead { get; } = DefaultExternalLookAhead;
        private EthereumTokens.Erc20Config Currency => Account.Currencies.Get<EthereumTokens.Erc20Config>(Account.Currency);
        private Erc20Account Account { get; }
        private EthereumAccount EthereumAccount { get; }

        public Erc20WalletScanner(Erc20Account account, EthereumAccount ethereumAccount)
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
//               new {Chain = HdKeyStorage.NonHdKeysChain, LookAhead = 0},
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

            foreach (var param in scanParams)
            {
                var freeKeysCount = 0;
                var index = 0u;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var walletAddress = await Account
                        .DivideAddressAsync(
                            chain: param.Chain,
                            index: index,
                            keyType: CurrencyConfig.ClassicKey)
                        .ConfigureAwait(false);

                    if (walletAddress == null)
                        break;

                    Log.Debug(
                        "Scan transactions for {@name} address {@chain}:{@index}:{@address}",
                        currency.Name,
                        param.Chain,
                        index,
                        walletAddress.Address);

                    var events = await GetERC20EventsAsync(walletAddress.Address, cancellationToken);

                    if (events == null || !events.Any())
                    {
                        var ethereumAddress = await EthereumAccount
                            .GetAddressAsync(walletAddress.Address, cancellationToken)
                            .ConfigureAwait(false);

                        if (ethereumAddress != null && ethereumAddress.HasActivity)
                        {
                            freeKeysCount = 0;
                            index++;
                            continue;
                        }

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
                            var tx = new EthereumTransaction();

                            if (ev.IsERC20ApprovalEvent())
                                tx = ev.TransformApprovalEvent(currency, lastBlockNumber);
                            else if (ev.IsERC20TransferEvent())
                                tx = ev.TransformTransferEvent(walletAddress.Address, currency, lastBlockNumber);

                            if (tx != null)
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

            if (lastBlockNumberResult == null)
            {
                Log.Error("Connection error while get block number");
                return;
            }

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

            var events = await GetERC20EventsAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (events == null || !events.Any()) // address without activity
                return;

            foreach (var ev in events)
            {
                var tx = new EthereumTransaction();
    
                if (ev.IsERC20ApprovalEvent())
                    tx = ev.TransformApprovalEvent(currency, lastBlockNumber);
                else if (ev.IsERC20TransferEvent())
                    tx = ev.TransformTransferEvent(address, currency, lastBlockNumber);

                if (tx != null)
                    txs.Add(tx);
            }

            if (txs.Any())
                await UpsertTransactionsAsync(txs)
                    .ConfigureAwait(false);

            await Account
                .UpdateBalanceAsync(address: address, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<List<ContractEvent>> GetERC20EventsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var currency = Currency;
            var api = new EtherScanApi(currency);

            var approveEventsResult = await api
                .GetContractEventsAsync(
                    address: currency.ERC20ContractAddress,
                    fromBlock: currency.SwapContractBlockNumber,
                    toBlock: ulong.MaxValue,
                    topic0: EventSignatureExtractor.GetSignatureHash<ERC20ApprovalEventDTO>(),
                    topic1: "0x000000000000000000000000" + address.Substring(2),
                    topic2: "0x000000000000000000000000" + currency.SwapContractAddress.Substring(2),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (approveEventsResult == null)
            {
                Log.Error("Connection error while get approve events");
                return null;
            }

            if (approveEventsResult.HasError)
            {
                Log.Error(
                    "Error while scan address transactions for {@address} with code {@code} and description {@description}",
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
                    topic0: EventSignatureExtractor.GetSignatureHash<ERC20TransferEventDTO>(),
                    topic1: "0x000000000000000000000000" + address.Substring(2),
                    topic2: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (outEventsResult == null)
            {
                Log.Error("Connection error while get output events");
                return null;
            }

            if (outEventsResult.HasError)
            {
                Log.Error(
                    "Error while scan address transactions for {@address} with code {@code} and description {@description}",
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
                    topic0: EventSignatureExtractor.GetSignatureHash<ERC20TransferEventDTO>(),
                    topic1: null,
                    topic2: "0x000000000000000000000000" + address.Substring(2),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (inEventsResult == null)
            {
                Log.Error("Connection error while get input events");
                return null;
            }

            if (inEventsResult.HasError)
            {
                Log.Error(
                    "Error while scan address transactions for {@address} with code {@code} and description {@description}",
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