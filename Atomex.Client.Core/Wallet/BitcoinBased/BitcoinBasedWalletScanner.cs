using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Bitcoin;
using Atomex.Common;
using Atomex.Wallets.Bips;
using Atomex.Wallets;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallet.BitcoinBased
{
    public class BitcoinBasedWalletScanner : ICurrencyWalletScanner
    {
        private class AddressScanResult
        {
            public IEnumerable<BitcoinTxOutput> Outputs { get; set; }
            public IEnumerable<BitcoinTransaction> Transactions { get; set; }
        }

        private const int DefaultInternalLookAhead = 3;
        private const int DefaultExternalLookAhead = 3;

        private readonly BitcoinBasedAccount _account;
        private BitcoinBasedConfig Config => _account.Currencies.Get<BitcoinBasedConfig>(_account.Currency);
        private int InternalLookAhead { get; } = DefaultInternalLookAhead;
        private int ExternalLookAhead { get; } = DefaultExternalLookAhead;

        public BitcoinBasedWalletScanner(BitcoinBasedAccount account)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task ScanAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var scanParams = new[]
                {
                    (KeyType : CurrencyConfig.StandardKey, Chain : Bip44.Internal, LookAhead : InternalLookAhead),
                    (KeyType : CurrencyConfig.StandardKey, Chain : Bip44.External, LookAhead : ExternalLookAhead),
                    (KeyType : BitcoinBasedConfig.SegwitKey, Chain : Bip44.Internal, LookAhead : InternalLookAhead),
                    (KeyType : BitcoinBasedConfig.SegwitKey, Chain : Bip44.External, LookAhead : ExternalLookAhead)
                };

                var api = Config.GetBitcoinBlockchainApi();

                var outputs = new List<BitcoinTxOutput>();
                var txs = new List<BitcoinTransaction>();
                var walletAddresses = new List<WalletAddress>();

                WalletAddress defautWalletAddress = null;

                foreach (var (keyType, chain, lookAhead) in scanParams)
                {
                    var freeKeysCount = 0;
                    var index = 0u;

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var keyPath = Config.GetKeyPathPattern(keyType)
                            .Replace(KeyPathExtensions.AccountPattern, KeyPathExtensions.DefaultAccount)
                            .Replace(KeyPathExtensions.ChainPattern, chain.ToString())
                            .Replace(KeyPathExtensions.IndexPattern, index.ToString());

                        var walletAddress = await _account
                            .DivideAddressAsync(
                                keyPath: keyPath,
                                keyType: keyType)
                            .ConfigureAwait(false);

                        if (keyType == CurrencyConfig.StandardKey &&
                            chain == Bip44.External &&
                            index == 0)
                        {
                            defautWalletAddress = walletAddress;
                        }

                        var (updateResult, error) = await UpdateAddressAsync(
                                walletAddress: walletAddress,
                                api: api,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (error != null)
                        {
                            Log.Error("[BitcoinBasedWalletScanner] ScanAsync error while scan {@address}", walletAddress.Address);
                            return;
                        }

                        if (!walletAddress.HasActivity && !updateResult.Outputs.Any()) // address without activity
                        {
                            freeKeysCount++;

                            if (freeKeysCount >= lookAhead)
                            {
                                Log.Debug("[BitcoinBasedWalletScanner] {@lookAhead} free keys found. Chain scan completed", lookAhead);
                                break;
                            }
                        }
                        else // address has activity
                        {
                            freeKeysCount = 0;

                            outputs.AddRange(updateResult.Outputs);
                            txs.AddRange(updateResult.Transactions);

                            // save only active addresses
                            walletAddresses.Add(walletAddress);
                        }

                        index++;
                    }
                }

                if (outputs.Any())
                {
                    var upsertResult = await _account
                        .LocalStorage
                        .UpsertOutputsAsync(
                            outputs: outputs,
                            currency: _account.Currency,
                            network: Config.Network,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                if (txs.Any())
                {
                    var upsertResult = await _account
                        .LocalStorage
                        .UpsertTransactionsAsync(
                            txs,
                            notifyIfNewOrChanged: true,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                if (!walletAddresses.Any())
                    walletAddresses.Add(defautWalletAddress);

                var _ = await _account
                    .LocalStorage
                    .UpsertAddressesAsync(
                        walletAddresses: walletAddresses,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("[BitcoinBasedWalletScanner] ScanAsync canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "[BitcoinBasedWalletScanner] ScanAsync error: {@message}", e.Message);
            }
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

                var api = Config.GetBitcoinBlockchainApi();
                var outputs = new List<BitcoinTxOutput>();
                var txs = new List<BitcoinTransaction>();

                foreach (var walletAddress in walletAddresses)
                {
                    if (skipUsed && walletAddress.IsDisabled)
                        continue;

                    var (updateResult, error) = await UpdateAddressAsync(
                            walletAddress,
                            api: api,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (error != null)
                    {
                        Log.Error("[BitcoinBasedWalletScanner] UpdateBalanceAsync error while scan {@address}", walletAddress.Address);
                        return;
                    }

                    outputs.AddRange(updateResult.Outputs);
                    txs.AddRange(updateResult.Transactions);
                }

                if (outputs.Any())
                {
                    var upsertResult = await _account
                        .LocalStorage
                        .UpsertOutputsAsync(
                            outputs: outputs,
                            currency: _account.Currency,
                            network: Config.Network)
                        .ConfigureAwait(false);
                }

                if (txs.Any())
                {
                    var upsertResult = await _account
                        .LocalStorage
                        .UpsertTransactionsAsync(
                            txs,
                            notifyIfNewOrChanged: true,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                if (walletAddresses.Any())
                {
                    var _ = await _account
                        .LocalStorage
                        .UpsertAddressesAsync(
                            walletAddresses: walletAddresses,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                Log.Debug("[BitcoinBasedWalletScanner] UpdateBalanceAsync canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "[BitcoinBasedWalletScanner] UpdateBalanceAsync error: {@message}", e.Message);
            }
        }

        public async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("[BitcoinBasedWalletScanner] UpdateBalanceAsync for address {@address}", address);

                var walletAddress = await _account
                    .LocalStorage
                    .GetAddressAsync(
                        currency: _account.Currency,
                        address: address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (walletAddress == null)
                {
                    Log.Error("[BitcoinBasedWalletScanner] UpdateBalanceAsync error. Can't find address {@address} in local db", address);
                    return;
                }

                var (updateResult, error) = await UpdateAddressAsync(
                        walletAddress,
                        api: null,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                {
                    Log.Error("[BitcoinBasedWalletScanner] UpdateBalanceAsync error while scan {@address}", address);
                    return;
                }

                if (updateResult.Outputs.Any())
                {
                    await _account
                        .LocalStorage
                        .UpsertOutputsAsync(
                            outputs: updateResult.Outputs,
                            currency: _account.Currency,
                            network: Config.Network,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                if (updateResult.Transactions.Any())
                {
                    var upsertResult = await _account
                        .LocalStorage
                        .UpsertTransactionsAsync(
                            updateResult.Transactions,
                            notifyIfNewOrChanged: true,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                var _ = await _account
                    .LocalStorage
                    .UpsertAddressAsync(
                        walletAddress: walletAddress,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            }
            catch (OperationCanceledException)
            {
                Log.Debug("[BitcoinBasedWalletScanner] UpdateBalanceAsync canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "[BitcoinBasedWalletScanner] UpdateBalanceAsync error: {@message}", e.Message);
            }
        }

        private async Task<Result<AddressScanResult>> UpdateAddressAsync(
            WalletAddress walletAddress,
            BitcoinBlockchainApi api = null,
            CancellationToken cancellationToken = default)
        {
            var updateTimeStamp = DateTime.UtcNow;

            api ??= Config.GetBitcoinBlockchainApi();

            var (addressInfo, addressInfoError) = await api
                .GetAddressInfo(walletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            if (addressInfoError != null)
            {
                Log.Error("[BitcoinBasedWalletScanner] UpdateAddressAsync error while getting address info for {@address} with code {@code} and message {@message}",
                    walletAddress.Address,
                    addressInfoError.Value.Code,
                    addressInfoError.Value.Message);

                return addressInfoError;
            }

            walletAddress.Balance               = addressInfo.Balance;
            walletAddress.UnconfirmedIncome     = addressInfo.UnconfirmedIncome;
            walletAddress.UnconfirmedOutcome    = addressInfo.UnconfirmedOutcome;
            walletAddress.HasActivity           = addressInfo.Outputs.Any();
            walletAddress.LastSuccessfullUpdate = updateTimeStamp;

            if (!addressInfo.Outputs.Any())
                return new Result<AddressScanResult> {
                    Value = new AddressScanResult {
                        Outputs = addressInfo.Outputs,
                        Transactions = Enumerable.Empty<BitcoinTransaction>()
                    }
                };

            var (txs, txsError) = await UpdateTransactionsAsync(addressInfo.Outputs, cancellationToken)
                .ConfigureAwait(false);

            if (txsError != null)
            {
                Log.Error("[BitcoinBasedWalletScanner] UpdateAddressAsync error while getting transactions for {@address} with code {@code} and message {@message}",
                    walletAddress.Address,
                    txsError.Value.Code,
                    txsError.Value.Message);

                return txsError;
            }

            return new Result<AddressScanResult> {
                Value = new AddressScanResult {
                    Outputs = addressInfo.Outputs,
                    Transactions = txs
                }
            };
        }

        [Obsolete("Transactions can be partially collected from outputs without full tx data requests")]
        private async Task<Result<IEnumerable<BitcoinTransaction>>> UpdateTransactionsAsync(
            IEnumerable<BitcoinTxOutput> outputs,
            CancellationToken cancellationToken = default)
        {
            var uniqueTxs = new Dictionary<string, BitcoinTransaction>();

            foreach (var output in outputs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var txIds = new List<string> { output.TxId };

                if (output.IsSpent)
                    txIds.AddRange(output.SpentTxPoints.Select(p => p.Hash));

                foreach (var txId in txIds)
                {
                    // skip already requested transactions
                    if (uniqueTxs.ContainsKey(txId))
                        continue;

                    var localTx = await _account.LocalStorage
                        .GetTransactionByIdAsync<BitcoinTransaction>(_account.Currency, txId)
                        .ConfigureAwait(false);

                    // request only not confirmed transactions
                    if (localTx != null && localTx.IsConfirmed)
                        continue;

                    Log.Debug("[BitcoinBasedWalletScanner] Scan {@currency} transaction {@txId}", _account.Currency, txId);

                    var (tx, error) = await Config
                        .GetBlockchainApi()
                        .GetTransactionAsync(txId, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (error != null)
                    {
                        Log.Error(
                            "[BitcoinBasedWalletScanner] Error while get transactions {@txId}. Code: {@code}. Message: {@message}",
                            txId,
                            error.Value.Code,
                            error.Value.Message);

                        continue;
                    }

                    if (tx == null)
                    {
                        Log.Warning("[BitcoinBasedWalletScanner] Wow! Transaction with id {@txId} not found", txId);
                        continue;
                    }

                    uniqueTxs.Add(txId, tx as BitcoinTransaction);
                }
            }

            return uniqueTxs.Values;
        }
    }
}