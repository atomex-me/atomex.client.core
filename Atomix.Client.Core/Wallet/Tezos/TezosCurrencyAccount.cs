using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.Tezos;
using Atomix.Common;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;
using Serilog;

namespace Atomix.Wallet.Tezos
{
    public class TezosCurrencyAccount : CurrencyAccount
    {
        public TezosCurrencyAccount(
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
            var amountMicroTez = amount.ToMicroTez();
            var feeMicroTez = fee.ToMicroTez();

            var addresses = (await SelectUnspentAddressesAsync(amount, fee, cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            if (!addresses.Any())
            {
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: "Insufficient funds");
            }

            var feePerTransactionInMtz = Math.Round(feeMicroTez / addresses.Count);

            if (feePerTransactionInMtz < Atomix.Tezos.DefaultFee)
            {
                return new Error(
                    code: Errors.InsufficientFee,
                    description: "Insufficient fee");
            }

            Log.Debug(
                "Fee per transaction {@feePerTransaction}",
                feePerTransactionInMtz);

            var requiredAmountInMtz = amountMicroTez;

            foreach (var (walletAddress, balanceInTz) in addresses)
            {
                var txAmountInMtz = Math.Min(balanceInTz.ToMicroTez() - feePerTransactionInMtz, requiredAmountInMtz);
                requiredAmountInMtz -= txAmountInMtz;

                Log.Debug(
                    "Send {@amount} XTZ from address {@address} with balance {@balance}",
                    txAmountInMtz,
                    walletAddress.Address,
                    balanceInTz);

                var tx = new TezosTransaction
                {
                    From = walletAddress.Address,
                    To = to,
                    Amount = Math.Round(txAmountInMtz, 0),
                    Fee = feePerTransactionInMtz,
                    GasLimit = Atomix.Tezos.DefaultGasLimit,
                    StorageLimit = Atomix.Tezos.DefaultStorageLimit,
                    Type = TezosTransaction.OutputTransaction
                };

                var signResult = await Wallet
                    .SignAsync(tx, walletAddress.Address, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                {
                    return new Error(
                        code: Errors.TransactionSigningError,
                        description: "Transaction signing error");
                }

                var txId = await Currency.BlockchainApi
                    .BroadcastAsync(tx, cancellationToken)
                    .ConfigureAwait(false);

                Log.Debug(
                    messageTemplate: "Transaction successfully sent with txId: {@id}",
                    propertyValue: txId);

                await AddUnconfirmedTransactionAsync(
                        tx: tx,
                        selfAddresses: new[] { tx.From },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            return null;
        }

        public override async Task<decimal> EstimateFeeAsync(
            decimal amount,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var defaultFeeInTz = ((decimal) Atomix.Tezos.DefaultFee).ToTez();

            var addresses = await SelectUnspentAddressesAsync(
                    amount: amount,
                    fee: defaultFeeInTz,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return addresses.Count() * defaultFeeInTz;
        }

        public override async Task AddConfirmedTransactionAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = await TransactionRepository
                .AddTransactionAsync(tx)
                .ConfigureAwait(false);

            if (!result)
                return; // TODO: return result

            RaiseBalanceUpdated(new CurrencyEventArgs(tx.Currency));
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

            if (notify)
                RaiseUnconfirmedTransactionAdded(new TransactionEventArgs(tx));

            RaiseBalanceUpdated(new CurrencyEventArgs(tx.Currency));
        }

        public override async Task<decimal> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var addressBalances = await GetAddressBalancesAsync(cancellationToken)
                .ConfigureAwait(false);

            return addressBalances
                .FirstOrDefault(pair => pair.Item1.Address.ToLowerInvariant().Equals(address.ToLowerInvariant()))
                .Item2;
        }

        public override async Task<decimal> GetBalanceAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var addressBalances = await GetAddressBalancesAsync(cancellationToken)
                .ConfigureAwait(false);

            return addressBalances.Sum(pair => pair.Item2);
        }

        public override async Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            decimal requiredAmount,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var unspentAddressBalances = (await GetUnspentAddressBalancesAsync(cancellationToken))
                .ToList();

            var usedAddresses = new List<WalletAddress>();
            var usedAmount = 0m;

            foreach (var (walletAddress, balance) in unspentAddressBalances)
            {
                if (usedAmount >= requiredAmount)
                    break;

                usedAddresses.Add(walletAddress);
                usedAmount += balance;
            }

            if (requiredAmount > 0 && !usedAddresses.Any())
                throw new Exception($"Insufficient funds for currency {Currency.Name}");

            return usedAddresses;
        }

        public override async Task<bool> IsAddressHasOperationsAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return (await TransactionRepository.GetTransactionsAsync(Currency)
                .ConfigureAwait(false))
                .Cast<TezosTransaction>()
                .Any(t => t.From.ToLowerInvariant().Equals(walletAddress.Address.ToLowerInvariant()) ||
                          t.To.ToLowerInvariant().Equals(walletAddress.Address.ToLowerInvariant()));
        }

        public override Task<WalletAddress> GetRefundAddressAsync(
            IEnumerable<WalletAddress> paymentAddresses,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.FromResult(paymentAddresses.First()); // todo: check address balance and reserved amount
        }

        public override async Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var addressBalances = await GetUnspentAddressBalancesAsync(cancellationToken)
                .ConfigureAwait(false);

            var redeemFeeInTz = ((decimal) Atomix.Tezos.DefaultRedeemFee).ToTez();

            foreach (var (address, balanceInTz) in addressBalances)
                if (balanceInTz >= redeemFeeInTz)
                    return address;

            throw new Exception("Insufficient funds for redeem");
        }

        private async Task<IEnumerable<(WalletAddress, decimal)>> GetAddressBalancesAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var transactions = (await TransactionRepository
                .GetTransactionsAsync(Currency)
                .ConfigureAwait(false))
                .Cast<TezosTransaction>();

            var addressBalances = new Dictionary<string, (WalletAddress, decimal)>();

            foreach (var tx in transactions)
            {
                var addresses = new List<string>();

                if (tx.Type == TezosTransaction.OutputTransaction || tx.Type == TezosTransaction.SelfTransaction)
                    addresses.Add(tx.From);
                if (tx.Type == TezosTransaction.InputTransaction || tx.Type == TezosTransaction.SelfTransaction)
                    addresses.Add(tx.To);

                foreach (var address in addresses)
                {
                    var walletAddress = await Wallet
                        .GetAddressAsync(Currency, address, cancellationToken)
                        .ConfigureAwait(false);

                    var isReceive = address.ToLowerInvariant().Equals(tx.To.ToLowerInvariant());

                    var signedAmount = isReceive
                        ? tx.Amount
                        : -(tx.Amount + tx.Fee);

                    if (addressBalances.TryGetValue(address, out var pair)) {
                        addressBalances[address] = (pair.Item1, pair.Item2 + signedAmount.ToTez());
                    } else {
                        addressBalances.Add(address, (walletAddress, signedAmount.ToTez()));
                    }
                }
            }

            return addressBalances.Values;
        }

        private async Task<IEnumerable<(WalletAddress, decimal)>> GetUnspentAddressBalancesAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return (await GetAddressBalancesAsync(cancellationToken)
                .ConfigureAwait(false))
                .Where(p => p.Item2 > 0);
        }

        private async Task<IEnumerable<(WalletAddress, decimal)>> SelectUnspentAddressesAsync(
            decimal amount,
            decimal fee,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // TODO: unspent address using policy (transaction count minimization?)

            var unspentAddressBalances = (await GetUnspentAddressBalancesAsync(cancellationToken)
                .ConfigureAwait(false))
                .ToList()
                .SortList((a, b) => a.Item2.CompareTo(b.Item2));

            if (unspentAddressBalances.Count == 0)
                return unspentAddressBalances;

            for (var txCount = 1; txCount <= unspentAddressBalances.Count; ++txCount)
            {
                // fee amount per transaction
                var feeAmountInTz = fee / txCount;

                var usedAddressed = new List<(WalletAddress, decimal)>();
                var usedAmount = 0m;

                foreach (var (walletAddress, balanceInTz) in unspentAddressBalances)
                {
                    if (balanceInTz < feeAmountInTz)
                        continue; // ignore addresses with balance less than fee amount

                    usedAmount += balanceInTz - feeAmountInTz;
                    usedAddressed.Add((walletAddress, balanceInTz));

                    if (usedAmount >= amount)
                        break;
                }

                if (usedAmount < amount)
                    continue;

                return usedAddressed;
            }

            return Enumerable.Empty<(WalletAddress, decimal)>();
        }
    }
}