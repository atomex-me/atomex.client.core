using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Common;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;
using Serilog;

namespace Atomix.Wallet.BitcoinBased
{
    public class BitcoinBasedCurrencyAccount : CurrencyAccount, IAddressResolver
    {
        private BitcoinBasedCurrency BtcBasedCurrency => (BitcoinBasedCurrency) Currency;

        public BitcoinBasedCurrencyAccount(
            Currency currency,
            IHdWallet wallet,
            IAccountDataRepository dataRepository)
                : base(currency, wallet, dataRepository)
        {
        }

        #region Common

        public override async Task<Error> SendAsync(
            IEnumerable<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var unspentOutputs = (await DataRepository
                .GetAvailableOutputsAsync(Currency)
                .ConfigureAwait(false))
                .Where(o => from.FirstOrDefault(w => w.Address == o.DestinationAddress(Currency)) != null)
                .ToList();

            return await SendAsync(
                    outputs: unspentOutputs,
                    to: to,
                    amount: amount,
                    fee: fee,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<Error> SendAsync(
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var unspentOutputs = (await DataRepository
                .GetAvailableOutputsAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            return await SendAsync(
                    outputs: unspentOutputs,
                    to: to,
                    amount: amount,
                    fee: fee,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<Error> SendAsync(
            List<ITxOutput> outputs,
            string to,
            decimal amount,
            decimal fee,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var amountSatoshi = BtcBasedCurrency.CoinToSatoshi(amount);
            var feeSatoshi = BtcBasedCurrency.CoinToSatoshi(fee);

            outputs = outputs
                .SelectOutputsForAmount(amountSatoshi + feeSatoshi)
                .ToList();

            if (!outputs.Any())
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: "Insufficient funds");

            var changeAddress = await GetFreeInternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            var tx = BtcBasedCurrency
                .CreatePaymentTx(
                    unspentOutputs: outputs,
                    destinationAddress: to,
                    changeAddress: changeAddress.Address,
                    amount: amountSatoshi,
                    fee: feeSatoshi);

            var result = await Wallet
                .SignAsync(
                    tx: tx,
                    spentOutputs: outputs,
                    addressResolver: this,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!result)
                return new Error(
                    code: Errors.TransactionSigningError,
                    description: "Transaction signing error");

            if (!tx.Verify(outputs))
                return new Error(
                    code: Errors.TransactionVerificationError,
                    description: "Transaction verification error");

            var txId = await Currency.BlockchainApi
                .BroadcastAsync(tx, cancellationToken)
                .ConfigureAwait(false);

            if (txId == null)
                return new Error(
                    code: Errors.TransactionBroadcastError,
                    description: "Transaction id is null");

            Log.Debug("Transaction successfully sent with txId: {@id}", txId);

            await UpsertTransactionAsync(
                    tx: tx,
                    updateBalance: true,
                    notifyIfUnconfirmed: true,
                    notifyIfBalanceUpdated: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return null;
        }

        public override async Task<decimal> EstimateFeeAsync(
            string to,
            decimal amount,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var amountInSatoshi = BtcBasedCurrency.CoinToSatoshi(amount);

            var unspentOutputs = (await DataRepository
                .GetAvailableOutputsAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            var feeInSatoshi = 0L;

            while (true)
            {
                var selectedOutputs = unspentOutputs
                    .SelectOutputsForAmount(amountInSatoshi + feeInSatoshi)
                    .ToList();

                if (!selectedOutputs.Any()) // insufficient funds
                {
                    var tx = BtcBasedCurrency
                        .CreatePaymentTx(unspentOutputs,
                            destinationAddress: BtcBasedCurrency.TestAddress(),
                            changeAddress: BtcBasedCurrency.TestAddress(),
                            amount: unspentOutputs.Sum(o => o.Value),
                            fee: 0);

                    return (long)(tx.VirtualSize() * BtcBasedCurrency.FeeRate) / (decimal)BtcBasedCurrency.DigitsMultiplier;
                }

                var testTx = BtcBasedCurrency
                    .CreatePaymentTx(selectedOutputs,
                        destinationAddress: BtcBasedCurrency.TestAddress(),
                        changeAddress: BtcBasedCurrency.TestAddress(),
                        amount: amountInSatoshi,
                        fee: feeInSatoshi);

                var requiredFeeInSatoshi = (long)(testTx.VirtualSize() * BtcBasedCurrency.FeeRate);

                if (requiredFeeInSatoshi > feeInSatoshi)
                {
                    feeInSatoshi = requiredFeeInSatoshi;
                    continue; 
                }

                return requiredFeeInSatoshi / (decimal)BtcBasedCurrency.DigitsMultiplier;
            }
        }

        #endregion Common

        #region Balances

        public override async Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var outputs = (await DataRepository
                .GetOutputsAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            //var unconfirmedTxs = (await DataRepository
            //    .GetUnconfirmedTransactionsAsync(Currency)
            //    .ConfigureAwait(false))
            //    .Cast<IInOutTransaction>()
            //    .ToList();

            // calculate balances
            var totalBalance = 0m;
            var totalUnconfirmedIncome = 0m;
            var totalUnconfirmedOutcome = 0m;
            var addressBalances = new Dictionary<string, WalletAddress>();

            foreach (var o in outputs)
            {
                var address = o.DestinationAddress(Currency);
                var amount = o.Value / (decimal) Currency.DigitsMultiplier;

                var isSpent = o.IsSpent;

                //var isConfirmedOutput = unconfirmedTxs
                //    .FirstOrDefault(t => t.Outputs
                //        .FirstOrDefault(to => to.Index == o.Index && to.TxId == o.TxId) != null) == null;

                var isConfirmedOutput = (await DataRepository
                    .GetTransactionByIdAsync(Currency, o.TxId)
                    .ConfigureAwait(false))
                    .IsConfirmed();

                //var isConfirmedInput = isSpent && unconfirmedTxs
                //    .FirstOrDefault(t => t.Inputs
                //        .FirstOrDefault(ti => ti.Index == o.Index && ti.Hash == o.TxId) != null) == null;

                var isConfirmedInput = isSpent && (await DataRepository
                    .GetTransactionByIdAsync(Currency, o.SpentTxPoint.Hash)
                    .ConfigureAwait(false))
                    .IsConfirmed();

                // balance = sum (all confirmed unspended outputs) + sum(all confirmed spent outputs with unconfirmed spent tx)
                // unconfirmedIncome = sum(all unconfirmed unspended outputs)
                // unconfirmedOutcome = -sum(all confirmed spent outputs with unconfirmed spent tx)

                if (addressBalances.TryGetValue(address, out var walletAddress))
                {
                    walletAddress.Balance += isConfirmedOutput && (!isSpent || !isConfirmedInput) ? amount : 0;
                    walletAddress.UnconfirmedIncome += !isConfirmedOutput && !isSpent ? amount : 0;
                    walletAddress.UnconfirmedOutcome += isConfirmedOutput && isSpent && !isConfirmedInput ? -amount : 0;
                }
                else
                {
                    walletAddress = await DataRepository
                        .GetWalletAddressAsync(Currency, address)
                        .ConfigureAwait(false);

                    walletAddress.Balance = isConfirmedOutput && (!isSpent || !isConfirmedInput) ? amount : 0;
                    walletAddress.UnconfirmedIncome = !isConfirmedOutput && !isSpent ? amount : 0;
                    walletAddress.UnconfirmedOutcome = isConfirmedOutput && isSpent && !isConfirmedInput ? -amount : 0;
                    walletAddress.HasActivity = true;

                    addressBalances.Add(address, walletAddress);
                }

                totalBalance += isConfirmedOutput && (!isSpent || !isConfirmedInput) ? amount : 0;
                totalUnconfirmedIncome += !isConfirmedOutput && !isSpent ? amount : 0;
                totalUnconfirmedOutcome += isConfirmedOutput && isSpent && !isConfirmedInput ? -amount : 0;
            }

            // upsert addresses
            await DataRepository
                .UpsertAddressesAsync(addressBalances.Values)
                .ConfigureAwait(false);

            Balance = totalBalance;
            UnconfirmedIncome = totalUnconfirmedIncome;
            UnconfirmedOutcome = totalUnconfirmedOutcome;

            RaiseBalanceUpdated(new CurrencyEventArgs(Currency));
        }

        public override async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var outputs = (await DataRepository
                .GetOutputsAsync(Currency, address)
                .ConfigureAwait(false))
                .ToList();

            var walletAddress = await DataRepository
                .GetWalletAddressAsync(Currency, address)
                .ConfigureAwait(false);

            var balance = 0m;
            var unconfirmedIncome = 0m;
            var unconfirmedOutcome = 0m;

            foreach (var o in outputs)
            {
                var amount = o.Value / (decimal) Currency.DigitsMultiplier;

                var isSpent = o.IsSpent;

                //var isConfirmedOutput = unconfirmedTxs
                //    .FirstOrDefault(t => t.Outputs
                //        .FirstOrDefault(to => to.Index == o.Index && to.TxId == o.TxId) != null) == null;

                var isConfirmedOutput = (await DataRepository
                    .GetTransactionByIdAsync(Currency, o.TxId)
                    .ConfigureAwait(false))
                    .IsConfirmed();

                //var isConfirmedInput = isSpent && unconfirmedTxs
                //    .FirstOrDefault(t => t.Inputs
                //        .FirstOrDefault(ti => ti.Index == o.Index && ti.Hash == o.TxId) != null) == null;

                var isConfirmedInput = isSpent && (await DataRepository
                    .GetTransactionByIdAsync(Currency, o.SpentTxPoint.Hash)
                    .ConfigureAwait(false))
                    .IsConfirmed();

                balance            += isConfirmedOutput && (!isSpent || !isConfirmedInput) ? amount : 0;
                unconfirmedIncome  += !isConfirmedOutput && !isSpent ? amount : 0;
                unconfirmedOutcome += isConfirmedOutput && isSpent && !isConfirmedInput ? -amount : 0;
            }

            var balanceDifference = balance - walletAddress.Balance;
            var unconfirmedIncomeDifference = unconfirmedIncome - walletAddress.UnconfirmedIncome;
            var unconfirmedOutcomeDifference = unconfirmedOutcome - walletAddress.UnconfirmedOutcome;

            if (balanceDifference != 0 ||
                unconfirmedIncomeDifference != 0 ||
                unconfirmedOutcomeDifference != 0)
            {
                walletAddress.Balance = balance;
                walletAddress.UnconfirmedIncome = unconfirmedIncome;
                walletAddress.UnconfirmedOutcome = unconfirmedOutcome;
                walletAddress.HasActivity = true;

                await DataRepository.UpsertAddressAsync(walletAddress)
                    .ConfigureAwait(false);

                Balance += balanceDifference;
                UnconfirmedIncome += unconfirmedIncomeDifference;
                UnconfirmedOutcome += unconfirmedOutcomeDifference;

                RaiseBalanceUpdated(new CurrencyEventArgs(Currency));
            }
        }

        #endregion Balances

        #region Addresses

        public override async Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool isFeePerTransaction,
            AddressUsagePolicy addressUsagePolicy,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // todo: unspent address using policy (transaction count minimization?)

            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            unspentAddresses = ApplyAddressUsagePolicy(
                    addresses: unspentAddresses,
                    amount: amount,
                    fee: fee,
                    feePrice: feePrice,
                    addressUsagePolicy: addressUsagePolicy)
                .ToList();

            if (unspentAddresses.Count == 0)
                return unspentAddresses;

            var requiredAmount = amount + fee;

            var usedAddresses = new List<WalletAddress>();
            var usedAmount = 0m;

            foreach (var walletAddress in unspentAddresses)
            {
                if (usedAmount >= requiredAmount)
                    break;

                usedAddresses.Add(walletAddress);
                usedAmount += walletAddress.AvailableBalance();
            }

            if (requiredAmount > 0 && usedAmount < requiredAmount)
                return Enumerable.Empty<WalletAddress>();

            return ResolvePublicKeys(usedAddresses);
        }

        #endregion Addresses

        #region Transactions

        public override async Task UpsertTransactionAsync(
            IBlockchainTransaction tx,
            bool updateBalance = false,
            bool notifyIfUnconfirmed = true,
            bool notifyIfBalanceUpdated = true,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = await DataRepository
                .UpsertTransactionAsync(tx)
                .ConfigureAwait(false);

            if (!result)
                return; // TODO: return result

            if (!(tx is IInOutTransaction inOutTx))
                throw new NotSupportedException(message: "Transaction has incorrect type");

            await UpsertOutputsAsync(
                    tx: inOutTx,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (updateBalance)
                await UpdateBalanceAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (notifyIfUnconfirmed && !tx.IsConfirmed())
                RaiseUnconfirmedTransactionAdded(new TransactionEventArgs(tx));

            if (updateBalance && notifyIfBalanceUpdated)
                RaiseBalanceUpdated(new CurrencyEventArgs(tx.Currency));
        }

        #endregion Transactions

        #region Outputs

        private async Task UpsertOutputsAsync(
            IInOutTransaction tx,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // update & save self outputs
            foreach (var output in tx.Outputs.Cast<BitcoinBasedTxOutput>())
            {
                if (output.IsSwapPayment || output.IsP2PkhSwapPayment)
                    continue;

                string address;

                try
                {
                    address = output.DestinationAddress(tx.Currency);
                }
                catch (Exception)
                {
                    Log.Warning("Can't parse address from output for tx id {@txId}", tx.Id);
                    continue;
                }

                var isSelfAddress = await IsSelfAddressAsync(address, cancellationToken)
                    .ConfigureAwait(false);

                if (!isSelfAddress)
                    continue;

                await UpsertOutputAsync(tx.Currency, output, address)
                    .ConfigureAwait(false);
            }

            // update & save self inputs
            for (uint i = 0; i < tx.Inputs.Length; ++i)
            {
                var input = tx.Inputs[i];
                
                var selfInput = await DataRepository
                    .GetOutputAsync(tx.Currency, input.Hash, input.Index)
                    .ConfigureAwait(false);

                if (selfInput == null)
                    continue;

                selfInput.SpentTxPoint = new TxPoint(i, tx.Id);

                await UpsertOutputAsync(tx.Currency, selfInput, selfInput.DestinationAddress(tx.Currency))
                    .ConfigureAwait(false);
            }
        }

        private async Task UpsertOutputAsync(
            Currency currency,
            ITxOutput output,
            string address)
        {
            var addressOutputs = (await DataRepository
                .GetOutputsAsync(currency, address)
                .ConfigureAwait(false))
                .ToList();

            addressOutputs.Add(output);

            await DataRepository
                .UpsertOutputsAsync(
                    outputs: addressOutputs.RemoveDuplicates(),
                    currency: currency,
                    address: address)
                .ConfigureAwait(false);
        }

        #endregion Outputs

        #region AddressResolver

        public Task<WalletAddress> ResolveAddressAsync(
            Currency currency,
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ResolveAddressAsync(address, cancellationToken);
        }

        #endregion AddressResolver
    }
}