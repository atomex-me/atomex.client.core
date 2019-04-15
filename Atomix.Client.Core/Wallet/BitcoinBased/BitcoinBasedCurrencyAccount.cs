using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain;
using Atomix.Blockchain.Abstract;
using Atomix.Common;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;
using Serilog;

namespace Atomix.Wallet.BitcoinBased
{
    public class BitcoinBasedCurrencyAccount : CurrencyAccount
    {
        private BitcoinBasedCurrency BtcBasedCurrency => (BitcoinBasedCurrency) Currency;

        private IDictionary<string, IList<ITxOutput>> MostLikelySpent { get; } =
            new ConcurrentDictionary<string, IList<ITxOutput>>();

        public BitcoinBasedCurrencyAccount(
            Currency currency,
            IHdWallet wallet,
            ITransactionRepository transactionRepository)
                : base(currency, wallet, transactionRepository)
        {
        }

        public override async Task<Error> SendAsync(
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var amountSatoshi = BtcBasedCurrency.CoinToSatoshi(amount);
            var feeSatoshi = BtcBasedCurrency.CoinToSatoshi(fee);

            var unspentOutputs = (await TransactionRepository
                .GetUnspentOutputsAsync(BtcBasedCurrency)
                .ConfigureAwait(false))
                .SelectOutputsForAmount(amountSatoshi + feeSatoshi)
                .ToList();

            if (!unspentOutputs.Any())
            {
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: "Insufficient funds");
            }

            var changeAddress = await GetFreeInternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            var tx = BtcBasedCurrency
                .CreatePaymentTx(
                    unspentOutputs: unspentOutputs,
                    destinationAddress: to,
                    changeAddress: changeAddress.Address,
                    amount: amountSatoshi,
                    fee: feeSatoshi);

            var signResult = await Wallet
                .SignAsync(tx, unspentOutputs, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
            {
                return new Error(
                    code: Errors.TransactionSigningError,
                    description: "Transaction signing error");
            }

            // TODO: verification

            var txId = await Currency.BlockchainApi
                .BroadcastAsync(tx, cancellationToken)
                .ConfigureAwait(false);

            Log.Debug(
                messageTemplate: "Transaction successfully sent with txId: {@id}",
                propertyValue: txId);

            await AddUnconfirmedTransactionAsync(
                    tx: tx,
                    selfAddresses: new[] {changeAddress.Address},
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return null;
        }

        public override async Task<decimal> EstimateFeeAsync(
            decimal amount,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var amountSatoshi = BtcBasedCurrency.CoinToSatoshi(amount);

            var outputs = (await TransactionRepository
                .GetUnspentOutputsAsync(Currency)
                .ConfigureAwait(false))
                .SelectOutputsForAmount(amountSatoshi)
                .ToList();

            if (!outputs.Any())
                return 0;

            var testTx = BtcBasedCurrency
                .CreatePaymentTx(outputs,
                    destinationAddress: BtcBasedCurrency.TestAddress(),
                    changeAddress: BtcBasedCurrency.TestAddress(),
                    amount: amountSatoshi,
                    fee: 0);

            return testTx.VirtualSize() * BtcBasedCurrency.FeeRate / BtcBasedCurrency.DigitsMultiplier;
        }

        public override async Task AddUnconfirmedTransactionAsync(
            IBlockchainTransaction tx,
            string[] selfAddresses,
            bool notify = true,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = await TransactionRepository
                .AddTransactionAsync(tx)
                .ConfigureAwait(false);

            if (!result)
                return; // TODO: return result

            if (!(tx is IInOutTransaction inOutTx))
                throw new NotSupportedException("Transaction has incorrect type");

            await AddUnconfirmedOutputsAsync(inOutTx, selfAddresses, cancellationToken)
                .ConfigureAwait(false);

            if (notify)
                RaiseUnconfirmedTransactionAdded(new TransactionEventArgs(tx));

            RaiseBalanceUpdated(new CurrencyEventArgs(tx.Currency));
        }

        private async Task AddUnconfirmedOutputsAsync(
            IInOutTransaction tx,
            string[] selfAddresses,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var outputs = (await TransactionRepository
                .GetOutputsAsync(tx.Currency)
                .ConfigureAwait(false))
                .ToList();

            // add self inputs to MostLikelySpent list
            foreach (var i in tx.Inputs)
            {
                var selfInput = outputs.FirstOrDefault(o => o.Index == i.Index && o.TxId == i.Hash);

                if (selfInput == null)
                    continue;

                if (MostLikelySpent.TryGetValue(tx.Currency.Name, out var outputsList)) {
                    outputsList.Add(selfInput);
                } else {
                    MostLikelySpent.Add(tx.Currency.Name, new List<ITxOutput> { selfInput });
                }
            }

            // save self outputs
            foreach (var o in tx.Outputs)
            {
                if (o.IsSwapPayment)
                    continue;

                var address = o.DestinationAddress(tx.Currency);

                if (!selfAddresses.Contains(address))
                    continue;

                var addressOutputs = (await TransactionRepository
                    .GetOutputsAsync(tx.Currency, address)
                    .ConfigureAwait(false))
                    .ToList();

                addressOutputs.Add(o);

                await TransactionRepository
                    .AddOutputsAsync(addressOutputs, tx.Currency, address)
                    .ConfigureAwait(false);
            }
        }

        public override async Task AddConfirmedTransactionAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            await UpdateTransactionType(tx, cancellationToken)
                .ConfigureAwait(false);

            var result = await TransactionRepository
                .AddTransactionAsync(tx)
                .ConfigureAwait(false);

            if (!result)
                return; // TODO: return result

            if (!(tx is IInOutTransaction inOutTx))
                throw new NotSupportedException("Transaction has incorrect type");

            await AddConfirmedOutputsAsync(inOutTx, cancellationToken)
                .ConfigureAwait(false);

            RaiseBalanceUpdated(new CurrencyEventArgs(tx.Currency));
        }

        private async Task AddConfirmedOutputsAsync(
            IInOutTransaction tx,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var outputs = (await TransactionRepository
                .GetOutputsAsync(tx.Currency)
                .ConfigureAwait(false))
                .ToList();

            for (uint i = 0; i < tx.Inputs.Length; ++i)
            {
                var txInput = tx.Inputs[i];
                var selfInput = outputs.FirstOrDefault(o => o.Index == txInput.Index && o.TxId == txInput.Hash);

                if (selfInput == null)
                    continue;

                selfInput.SpentTxPoint = new TxPoint(i, tx.Id);

                var address = selfInput.DestinationAddress(tx.Currency);

                var addressOutputs = (await TransactionRepository
                    .GetOutputsAsync(tx.Currency, address)
                    .ConfigureAwait(false))
                    .ToList();

                addressOutputs.Add(selfInput);

                await TransactionRepository
                    .AddOutputsAsync(
                        outputs: addressOutputs.GroupBy(o => $"{o.TxId}{o.Index}", RemoveDuplicates),
                        currency: tx.Currency,
                        address: address)
                    .ConfigureAwait(false);

                // remove from most likely spent list
                if (MostLikelySpent.TryGetValue(tx.Currency.Name, out var mostLikelySpent))
                {
                    var unspentOutput = mostLikelySpent
                        .FirstOrDefault(o => o.TxId == selfInput.TxId && o.Index == selfInput.Index);

                    if (unspentOutput != null)
                        MostLikelySpent[tx.Currency.Name].Remove(unspentOutput);
                }
            }
        }

        public override Task UpdateTransactionType(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.CompletedTask; // nothing todo for BitcoinBased currencies
        }

        public override async Task<decimal> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var outputs = await TransactionRepository
                .GetUnspentOutputsAsync(Currency, address)
                .ConfigureAwait(continueOnCapturedContext: false);

            return (decimal) outputs.Sum(o => o.Value) / Currency.DigitsMultiplier;
        }

        public override async Task<decimal> GetBalanceAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var outputs = await TransactionRepository
                .GetUnspentOutputsAsync(Currency)
                .ConfigureAwait(continueOnCapturedContext: false);

            return (decimal)outputs.Sum(o => o.Value) / Currency.DigitsMultiplier;
        }

        public override async Task<bool> IsAddressHasOperationsAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var outputs = await TransactionRepository
                .GetOutputsAsync(Currency, walletAddress.Address)
                .ConfigureAwait(false);

            return outputs != null && outputs.Any();
        }

        public override async Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            decimal requiredAmount,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var amount = (long)(requiredAmount * Currency.DigitsMultiplier);

            var outputs = await TransactionRepository
                .GetUnspentOutputsAsync(Currency)
                .ConfigureAwait(false);

            // TODO: filter already used outputs

            var addresses = outputs
                .SelectAddressesForAmount(Currency, amount)
                .ToList();

            if (amount > 0 && !addresses.Any())
                throw new Exception($"Insufficient funds for currency {Currency.Name}");

            return await Task.WhenAll(addresses.Select(a => Wallet.GetAddressAsync(Currency, a)))
                .ConfigureAwait(false);
        }

        public override Task<WalletAddress> GetRefundAddressAsync(
            IEnumerable<WalletAddress> paymentAddresses,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetFreeInternalAddressAsync(cancellationToken);
        }

        public override Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetFreeInternalAddressAsync(cancellationToken);
        }

        private static ITxOutput RemoveDuplicates(string id, IEnumerable<ITxOutput> outputs)
        {
            var txOutputs = outputs.ToList();

            return txOutputs.Count == 1
                ? txOutputs.First()
                : txOutputs.First(o => o.IsSpent);
        }
    }
}