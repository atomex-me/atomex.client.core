using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.Ethereum;
using Atomix.Common;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;
using Serilog;

namespace Atomix.Wallet.Ethereum
{
    public class EthereumCurrencyAccount : CurrencyAccount
    {
        public EthereumCurrencyAccount(
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
            var addresses = (await SelectUnspentAddressesAsync(amount, fee, feePrice, cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            if (!addresses.Any())
            {
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: "Insufficient funds");
            }

            var feePerTransaction = Math.Round(fee / addresses.Count);

            if (feePerTransaction < Atomix.Ethereum.DefaultGasLimit)
            {
                return new Error(
                    code: Errors.InsufficientGas,
                    description: "Insufficient gas");
            }

            var feeAmount = Currencies.Eth.GetFeeAmount(feePerTransaction, feePrice);

            Log.Debug(
                "Fee per transaction {@feePerTransaction}. Fee Amount {@feeAmount}",
                feePerTransaction,
                feeAmount);

            var requiredAmount = amount;

            foreach (var (walletAddress, balance) in addresses)
            {
                var nonce = await ((IEthereumBlockchainApi)Currency.BlockchainApi)
                    .GetTransactionCountAsync(walletAddress.Address, cancellationToken)
                    .ConfigureAwait(false);

                var txAmount = Math.Min(balance - feeAmount, requiredAmount);
                requiredAmount -= txAmount;

                Log.Debug(
                    "Send {@amount} ETH from address {@address} with balance {@balance}",
                    txAmount,
                    walletAddress.Address,
                    balance);

                // TODO: check rest for last address (if rest less than DefaultGasLimit there is no reason to use this address)

                var tx = new EthereumTransaction
                {
                    To = to,
                    Amount = new BigInteger(Atomix.Ethereum.EthToWei(txAmount)),
                    Nonce = nonce,
                    GasPrice = new BigInteger(Atomix.Ethereum.GweiToWei(feePrice)),
                    GasLimit = new BigInteger(feePerTransaction),
                    Type = EthereumTransaction.OutputTransaction
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

                // TODO: verification
                //var result = tx.Verify();

                var txId = await Currency.BlockchainApi
                    .BroadcastAsync(tx, cancellationToken)
                    .ConfigureAwait(false);

                Log.Debug(
                    messageTemplate: "Transaction successfully sent with txId: {@id}",
                    propertyValue: txId);

                await AddUnconfirmedTransactionAsync(
                        tx: tx,
                        selfAddresses: new[] {tx.From.ToLowerInvariant()},
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            return null;
        }

        public override async Task<decimal> EstimateFeeAsync(
            decimal amount,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var addresses = await SelectUnspentAddressesAsync(
                    amount: amount,
                    fee: Atomix.Ethereum.DefaultGasLimit,
                    feePrice: Atomix.Ethereum.DefaultGasPriceInGwei,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return addresses.Count() * Atomix.Ethereum.DefaultGasLimit;
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
                .FirstOrDefault(pair => pair.Item1.Address.Equals(address))
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

        private async Task<IEnumerable<(WalletAddress, decimal)>> GetAddressBalancesAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var transactions = (await TransactionRepository
                .GetTransactionsAsync(Currency)
                .ConfigureAwait(false))
                .Cast<EthereumTransaction>();

            var addressBalances = new Dictionary<string, (WalletAddress, decimal)>();

            foreach (var tx in transactions)
            {
                var addresses = new List<string>();

                if (tx.Type == EthereumTransaction.OutputTransaction || tx.Type == EthereumTransaction.SelfTransaction)
                    addresses.Add(tx.From.ToLowerInvariant());
                if (tx.Type == EthereumTransaction.InputTransaction || tx.Type == EthereumTransaction.SelfTransaction)
                    addresses.Add(tx.To.ToLowerInvariant());

                foreach (var address in addresses)
                {
                    var walletAddress = await Wallet
                        .GetAddressAsync(Currency, address, cancellationToken)
                        .ConfigureAwait(false);

                    var isReceive = address.Equals(tx.To.ToLowerInvariant());

                    var gas = tx.GasUsed != 0 ? tx.GasUsed : tx.GasLimit;

                    var signedAmount = isReceive
                        ? Atomix.Ethereum.WeiToEth(tx.Amount)
                        : -Atomix.Ethereum.WeiToEth(tx.Amount +  tx.GasPrice * gas);

                    if (addressBalances.TryGetValue(address, out var pair)) {
                        addressBalances[address] = (pair.Item1, pair.Item2 + signedAmount);
                    } else {
                        addressBalances.Add(address, (walletAddress, signedAmount));
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
            decimal feePrice,
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
                var feeAmount = Currencies.Eth.GetFeeAmount(fee, feePrice) / txCount;

                var usedAddressed = new List<(WalletAddress, decimal)>();
                var usedAmount = 0m;

                foreach (var (walletAddress, balance) in unspentAddressBalances)
                {
                    if (balance < feeAmount)
                        continue; // ignore addresses with balance less than fee amount

                    usedAmount += balance - feeAmount;
                    usedAddressed.Add((walletAddress, balance));

                    if (usedAmount >= amount)
                        break;
                }

                if (usedAmount < amount)
                    continue;

                return usedAddressed;
            }

            return Enumerable.Empty<(WalletAddress, decimal)>();
        }

        public override async Task<bool> IsAddressHasOperationsAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return (await TransactionRepository.GetTransactionsAsync(Currency)
                .ConfigureAwait(false))
                .Cast<EthereumTransaction>()
                .Any(t => t.From.ToLowerInvariant().Equals(walletAddress.Address) ||
                          t.To.ToLowerInvariant().Equals(walletAddress.Address));
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

            var redeemFeeInEth = Atomix.Ethereum.GetDefaultRedeemFeeAmount();

            foreach (var (address, balanceInEth) in addressBalances)
                if (balanceInEth >= redeemFeeInEth)
                    return address;

            throw new Exception("Insufficient funds for redeem");
        }
    }
}