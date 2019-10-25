using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Atomex.Wallet.Abstract;
using Serilog;

namespace Atomex.Wallet.BitcoinBased
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
            CancellationToken cancellationToken = default)
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
                    dustUsagePolicy: DustUsagePolicy.Warning,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<Error> SendAsync(
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default)
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
                    dustUsagePolicy: DustUsagePolicy.Warning,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Error> SendAsync(
            List<ITxOutput> outputs,
            string to,
            decimal amount,
            decimal fee,
            DustUsagePolicy dustUsagePolicy,
            CancellationToken cancellationToken = default)
        {
            var amountInSatoshi = BtcBasedCurrency.CoinToSatoshi(amount);
            var feeInSatoshi = BtcBasedCurrency.CoinToSatoshi(fee);
            var requiredInSatoshi = amountInSatoshi + feeInSatoshi;

            // minimum amount and fee control
            if (amountInSatoshi < BtcBasedCurrency.GetDust())
                return new Error(
                    code: Errors.InsufficientAmount,
                    description: $"Insufficient amount to send. Min non-dust amount {BtcBasedCurrency.GetDust()}, actual {amountInSatoshi}");

            outputs = outputs
                .SelectOutputsForAmount(requiredInSatoshi)
                .ToList();

            var availableInSatoshi = outputs.Sum(o => o.Value);

            if (!outputs.Any())
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: $"Insufficient funds. Required {requiredInSatoshi}, available {availableInSatoshi}");

            var changeAddress = await GetFreeInternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            // minimum change control
            var changeInSatoshi = availableInSatoshi - requiredInSatoshi;
            if (changeInSatoshi > 0 && changeInSatoshi < BtcBasedCurrency.GetDust())
            {
                switch (dustUsagePolicy)
                {
                    case DustUsagePolicy.Warning:
                        return new Error(
                            code: Errors.InsufficientAmount,
                            description: $"Change {changeInSatoshi} can be definded by the network as dust and the transaction will be rejected");
                    case DustUsagePolicy.AddToDestination:
                        amountInSatoshi += changeInSatoshi;
                        break;
                    case DustUsagePolicy.AddToFee:
                        feeInSatoshi += changeInSatoshi;
                        break;
                    default:
                        return new Error(
                            code: Errors.InternalError,
                            description: $"Unknown dust usage policy value {dustUsagePolicy}");
                }
            }

            var tx = BtcBasedCurrency.CreatePaymentTx(
                unspentOutputs: outputs,
                destinationAddress: to,
                changeAddress: changeAddress.Address,
                amount: amountInSatoshi,
                fee: feeInSatoshi,
                lockTime: DateTimeOffset.MinValue);

            var signResult = await Wallet
                .SignAsync(
                    tx: tx,
                    spentOutputs: outputs,
                    addressResolver: this,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
                return new Error(
                    code: Errors.TransactionSigningError,
                    description: "Transaction signing error");

            if (!tx.Verify(outputs, out var errors))
                return new Error(
                    code: Errors.TransactionVerificationError,
                    description: $"Transaction verification error: {string.Join(", ", errors.Select(e => e.Description))}");

            var broadcastResult = await Currency.BlockchainApi
                .BroadcastAsync(tx, cancellationToken)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
                return broadcastResult.Error;

            var txId = broadcastResult.Value;

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

        public override async Task<decimal?> EstimateFeeAsync(
            string to,
            decimal amount,
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default)
        {
            var amountInSatoshi = BtcBasedCurrency.CoinToSatoshi(amount);

            var availableOutputs = (await DataRepository
                .GetAvailableOutputsAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (!availableOutputs.Any())
                return null; // insufficient funds

            for (var i = 1; i <= availableOutputs.Count; ++i)
            {
                var selectedOutputs = availableOutputs
                    .Take(i)
                    .ToArray();

                var estimatedSigSize = BitcoinBasedCurrency.EstimateSigSize(selectedOutputs);

                var selectedInSatoshi = selectedOutputs.Sum(o => o.Value);

                if (selectedInSatoshi < amountInSatoshi) // insufficient funds
                    continue;

                var maxFeeInSatoshi = selectedInSatoshi - amountInSatoshi;

                var estimatedTx = BtcBasedCurrency
                    .CreatePaymentTx(
                        unspentOutputs: selectedOutputs,
                        destinationAddress: BtcBasedCurrency.TestAddress(),
                        changeAddress: BtcBasedCurrency.TestAddress(),
                        amount: amountInSatoshi,
                        fee: maxFeeInSatoshi,
                        lockTime: DateTimeOffset.MinValue);
                
                var estimatedTxVirtualSize = estimatedTx.VirtualSize();
                var estimatedTxSize = estimatedTxVirtualSize + estimatedSigSize;
                var estimatedTxSizeWithChange = estimatedTxVirtualSize + estimatedSigSize + BitcoinBasedCurrency.OutputSize;

                var estimatedFeeInSatoshi = (long)(estimatedTxSize * BtcBasedCurrency.FeeRate);

                if (estimatedFeeInSatoshi > maxFeeInSatoshi) // insufficient funds
                    continue;

                var estimatedChangeInSatoshi = selectedInSatoshi - amountInSatoshi - estimatedFeeInSatoshi;

                // if estimated change is dust
                if (estimatedChangeInSatoshi >= 0 && estimatedChangeInSatoshi < BtcBasedCurrency.GetDust())
                    return BtcBasedCurrency.SatoshiToCoin(estimatedFeeInSatoshi + estimatedChangeInSatoshi);

                // if estimated change > dust
                var estimatedFeeWithChangeInSatoshi = (long)(estimatedTxSizeWithChange * BtcBasedCurrency.FeeRate);

                if (estimatedFeeWithChangeInSatoshi > maxFeeInSatoshi) // insufficient funds
                    continue;

                var esitmatedNewChangeInSatoshi = selectedInSatoshi - amountInSatoshi - estimatedFeeWithChangeInSatoshi;

                // if new estimated change is dust
                if (esitmatedNewChangeInSatoshi >= 0 && esitmatedNewChangeInSatoshi < BtcBasedCurrency.GetDust())
                    return BtcBasedCurrency.SatoshiToCoin(estimatedFeeWithChangeInSatoshi + esitmatedNewChangeInSatoshi);

                // if new estimated change > dust
                return BtcBasedCurrency.SatoshiToCoin(estimatedFeeWithChangeInSatoshi);
            }

            return null; // insufficient funds
        }

        public override async Task<(decimal, decimal)> EstimateMaxAmountToSendAsync(
            string to,
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default)
        {
            var unspentOutputs = (await DataRepository
                .GetAvailableOutputsAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (!unspentOutputs.Any())
                return (0m, 0m);

            var availableAmountInSatoshi = unspentOutputs.Sum(o => o.Value);
            var estimatedSigSize = BitcoinBasedCurrency.EstimateSigSize(unspentOutputs);

            var testTx = BtcBasedCurrency
                .CreatePaymentTx(
                    unspentOutputs: unspentOutputs,
                    destinationAddress: BtcBasedCurrency.TestAddress(),
                    changeAddress: BtcBasedCurrency.TestAddress(),
                    amount: availableAmountInSatoshi,
                    fee: 0,
                    lockTime: DateTimeOffset.MinValue);

            // requiredFee = txSize * feeRate without dust, because all coins must be send to one address
            var requiredFeeInSatoshi = (long)((testTx.VirtualSize() + estimatedSigSize) * BtcBasedCurrency.FeeRate);

            var amount = BtcBasedCurrency.SatoshiToCoin(Math.Max(availableAmountInSatoshi - requiredFeeInSatoshi, 0));
            var fee = BtcBasedCurrency.SatoshiToCoin(requiredFeeInSatoshi);

            return (amount, fee);
        }

        protected override async Task ResolveTransactionTypeAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var outputs = await DataRepository
                .GetOutputsAsync(Currency)
                .ConfigureAwait(false);

            var indexedOutputs = outputs.ToDictionary(o => $"{o.TxId}:{o.Index}");

            var btcBasedTx = (IBitcoinBasedTransaction) tx;

            var selfInputs = btcBasedTx.Inputs
                .Where(i => indexedOutputs.ContainsKey($"{i.Hash}:{i.Index}"))
                .Select(i => indexedOutputs[$"{i.Hash}:{i.Index}"])
                .ToList();

            if (selfInputs.Any())
                btcBasedTx.Type |= BlockchainTransactionType.Output;

            var sentAmount = selfInputs.Sum(i => i.Value);

            // todo: recognize swap refund/redeem

            var selfOutputs = btcBasedTx.Outputs
                .Where(o => indexedOutputs.ContainsKey($"{o.TxId}:{o.Index}"))
                .ToList();

            if (selfOutputs.Any())
                btcBasedTx.Type |= BlockchainTransactionType.Input;

            var receivedAmount = selfOutputs.Sum(o => o.Value);

            btcBasedTx.Amount = receivedAmount - sentAmount;

            // todo: recognize swap payment

            var oldTx = await DataRepository
                .GetTransactionByIdAsync(Currency, tx.Id)
                .ConfigureAwait(false);

            if (oldTx != null)
                btcBasedTx.Type |= oldTx.Type;
        }

        #endregion Common

        #region Balances

        public override async Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
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

                var tx = await DataRepository
                    .GetTransactionByIdAsync(Currency, o.TxId)
                    .ConfigureAwait(false);

                var isConfirmedOutput = tx?.IsConfirmed ?? false;

                //var isConfirmedInput = isSpent && unconfirmedTxs
                //    .FirstOrDefault(t => t.Inputs
                //        .FirstOrDefault(ti => ti.Index == o.Index && ti.Hash == o.TxId) != null) == null;

                var isConfirmedInput = false;

                if (isSpent)
                {
                    var spentTx = await DataRepository
                        .GetTransactionByIdAsync(Currency, o.SpentTxPoint.Hash)
                        .ConfigureAwait(false);

                    isConfirmedInput = spentTx?.IsConfirmed ?? false;
                }

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
            CancellationToken cancellationToken = default)
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
                    .IsConfirmed;

                //var isConfirmedInput = isSpent && unconfirmedTxs
                //    .FirstOrDefault(t => t.Inputs
                //        .FirstOrDefault(ti => ti.Index == o.Index && ti.Hash == o.TxId) != null) == null;

                var isConfirmedInput = isSpent && (await DataRepository
                    .GetTransactionByIdAsync(Currency, o.SpentTxPoint.Hash)
                    .ConfigureAwait(false))
                    .IsConfirmed;

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
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default)
        {
            if (feeUsagePolicy == FeeUsagePolicy.EstimatedFee)
            {
                var estimatedFee = await EstimateFeeAsync(
                        to: null,
                        amount: amount,
                        type: transactionType,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (estimatedFee == null)
                    return Enumerable.Empty<WalletAddress>();

                fee = estimatedFee.Value;
            }

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
            CancellationToken cancellationToken = default)
        {
            if (!(tx is IBitcoinBasedTransaction btcBasedTx))
                throw new NotSupportedException("Transaction has incorrect type");

            await UpsertOutputsAsync(
                    tx: btcBasedTx,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ResolveTransactionTypeAsync(tx, cancellationToken)
                .ConfigureAwait(false);

            var result = await DataRepository
                .UpsertTransactionAsync(tx)
                .ConfigureAwait(false);

            if (!result)
                return; // TODO: return result

            if (updateBalance)
                await UpdateBalanceAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (notifyIfUnconfirmed && !tx.IsConfirmed)
                RaiseUnconfirmedTransactionAdded(new TransactionEventArgs(tx));

            if (updateBalance && notifyIfBalanceUpdated)
                RaiseBalanceUpdated(new CurrencyEventArgs(tx.Currency));
        }

        #endregion Transactions

        #region Outputs

        private async Task UpsertOutputsAsync(
            IInOutTransaction tx,
            CancellationToken cancellationToken = default)
        {
            // update & save self outputs
            foreach (var output in tx.Outputs.Cast<BitcoinBasedTxOutput>())
            {
                if (!output.IsP2Pk && !output.IsP2Pkh && !output.IsSegwitP2Pkh)
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
            CancellationToken cancellationToken = default)
        {
            return ResolveAddressAsync(address, cancellationToken);
        }

        #endregion AddressResolver
    }
}