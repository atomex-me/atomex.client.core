using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;
using NBitcoin;

using Atomex.Abstract;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Atomex.Common.Bson;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;

namespace Atomex.Wallet.BitcoinBased
{
    public class BitcoinBasedAccount : CurrencyAccount, IAddressResolver, IEstimatable
    {
        public BitcoinBasedConfig Config => Currencies.Get<BitcoinBasedConfig>(Currency);

        public BitcoinBasedAccount(
            string currency,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository)
                : base(currency, currencies, wallet, dataRepository)
        {
        }

        #region Common

        public async Task<Error> SendAsync(
            IEnumerable<BitcoinBasedTxOutput> from,
            string to,
            decimal amount,
            decimal fee,
            DustUsagePolicy dustUsagePolicy,
            CancellationToken cancellationToken = default)
        {
            var config = Config;

            var amountInSatoshi = config.CoinToSatoshi(amount);
            var feeInSatoshi = config.CoinToSatoshi(fee);
            var requiredInSatoshi = amountInSatoshi + feeInSatoshi;

            // minimum amount and fee control
            if (amountInSatoshi < config.GetDust())
                return new Error(
                    code: Errors.InsufficientAmount,
                    description: $"Insufficient amount to send. Min non-dust amount {config.GetDust()}, actual {amountInSatoshi}");

            from = from
                .SelectOutputsForAmount(requiredInSatoshi)
                .ToList();

            var availableInSatoshi = from.Sum(o => o.Value);

            if (!from.Any())
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: $"Insufficient funds. Required {requiredInSatoshi}, available {availableInSatoshi}");

            var changeAddress = await GetFreeInternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            // minimum change control
            var changeInSatoshi = availableInSatoshi - requiredInSatoshi;
            if (changeInSatoshi > 0 && changeInSatoshi < config.GetDust())
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

            var tx = config.CreatePaymentTx(
                unspentOutputs: from,
                destinationAddress: to,
                changeAddress: changeAddress.Address,
                amount: amountInSatoshi,
                fee: feeInSatoshi,
                lockTime: DateTimeOffset.MinValue);

            var signResult = await Wallet
                .SignAsync(
                    tx: tx,
                    spentOutputs: from,
                    addressResolver: this,
                    currencyConfig: config,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
                return new Error(
                    code: Errors.TransactionSigningError,
                    description: "Transaction signing error");

            if (!tx.Verify(from, out var errors, config))
                return new Error(
                    code: Errors.TransactionVerificationError,
                    description: $"Transaction verification error: {string.Join(", ", errors.Select(e => e.Description))}");

            var broadcastResult = await config.BlockchainApi
                .TryBroadcastAsync(tx, cancellationToken: cancellationToken)
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
                    updateBalance: false,
                    notifyIfUnconfirmed: true,
                    notifyIfBalanceUpdated: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _ = UpdateBalanceAsync(cancellationToken);

            return null;
        }

        public async Task<decimal?> EstimateFeeAsync(
            IEnumerable<BitcoinInputToSign> from,
            string to,
            string changeTo,
            decimal amount,
            decimal feeRate,
            CancellationToken cancellationToken = default)
        {
            var txParams = await BitcoinTransactionParams.SelectTransactionParamsByFeeRateAsync(
                    availableInputs: from,
                    destinations: new BitcoinDestination[] {
                        new BitcoinDestination
                        {
                            Script = BitcoinAddress.Create(to, Config.Network).ScriptPubKey,
                            AmountInSatoshi = Config.CoinToSatoshi(amount)
                        }
                    },
                    changeAddress: changeTo,
                    feeRate: feeRate,
                    currencyConfig: Config,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return txParams != null
                ? Config.SatoshiToCoin((long)txParams.FeeInSatoshi)
                : 0;
        }

        public async Task<decimal?> EstimateFeeAsync(
            IFromSource from,
            string to,
            decimal amount,
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default)
        {
            var feeRate = await Config
                .GetFeeRateAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var outputs = (from as FromOutputs)?.Outputs;

            if (outputs == null)
                return Config.SatoshiToCoin((long)(feeRate * BitcoinBasedConfig.OneInputTwoOutputTxSize));

            var changeAddress = await GetFreeInternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            return await EstimateFeeAsync(
                    from: outputs.Select(o => new BitcoinInputToSign { Output = o }),
                    to: to,
                    changeTo: changeAddress.Address,
                    amount: amount,
                    feeRate: feeRate,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<MaxAmountEstimation> EstimateMaxAmountToSendAsync(
            IFromSource from,
            string to,
            BlockchainTransactionType type,
            decimal fee = 0,
            decimal feePrice = 0,
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            if (fee != 0 && feePrice != 0)
                throw new ArgumentException("Parameters Fee and FeePrice cannot be used at the same time");

            var outputs = (from as FromOutputs)?.Outputs;

            if (outputs == null)
                return new MaxAmountEstimation {
                    Error = new Error(Errors.InsufficientFunds, "Insufficient funds")
                };

            var availableInSatoshi = outputs.Sum(o => o.Value);

            if (fee != 0)
            {
                var feeInSatoshi = Config.CoinToSatoshi(fee);

                return new MaxAmountEstimation {
                    Amount = Config.SatoshiToCoin(Math.Max(availableInSatoshi - feeInSatoshi, 0)),
                    Fee = Config.SatoshiToCoin(feeInSatoshi)
                };
            }

            var inputsToSign = outputs.Select(o => new BitcoinInputToSign { Output = o });
            var inputsSize = inputsToSign.Sum(i => i.SizeWithSignature());
            var witnessCount = outputs.Sum(o => o.IsSegWit ? 1 : 0);

            var destination = new BitcoinDestination
            {
                Script = BitcoinAddress.Create(to, Config.Network).ScriptPubKey,
            };

            var changeAddress = await GetFreeInternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            var (size, sizeWithChange) = BitcoinTransactionParams.CalculateTxSize(
                inputsCount: outputs.Count(),
                inputsSize: inputsSize,
                outputsCount: 1,
                outputsSize: destination.Size(),
                witnessCount: witnessCount,
                changeOutputSize: BitcoinTransactionParams.CalculateChangeOutputSize(changeAddress.Address, Config.Network));

            if (feePrice == 0)
            {
                feePrice = await Config
                    .GetFeeRateAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            var estimatedFeeInSatoshi = (long)(feePrice * size);

            if (availableInSatoshi < estimatedFeeInSatoshi) // not enough funds for a tx with one output
                return new MaxAmountEstimation {
                    Error = new Error(Errors.InsufficientFunds, "Insufficient funds")
                };

            return new MaxAmountEstimation
            {
                Amount = Config.SatoshiToCoin(availableInSatoshi - estimatedFeeInSatoshi),
                Fee = Config.SatoshiToCoin(estimatedFeeInSatoshi)
            };
        }

        protected override async Task<bool> ResolveTransactionTypeAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var currency = Config;

            var oldTx = await DataRepository
                .GetTransactionByIdAsync(Currency, tx.Id, currency.TransactionType)
                .ConfigureAwait(false);

            if (oldTx != null && oldTx.IsConfirmed)
                return false;

            var outputs = await DataRepository
                .GetOutputsAsync(Currency, currency.OutputType())
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

            if (oldTx != null)
                btcBasedTx.Type |= oldTx.Type;

            return true;
        }

        #endregion Common

        #region Balances

        public override Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var currency = Config;

                    var outputs = (await DataRepository
                        .GetOutputsAsync(Currency, currency.OutputType())
                        .ConfigureAwait(false))
                        .ToList();

                    // calculate balances
                    var totalBalance = 0m;
                    var totalUnconfirmedIncome = 0m;
                    var totalUnconfirmedOutcome = 0m;
                    var addressBalances = new Dictionary<string, WalletAddress>();

                    foreach (var o in outputs)
                    {
                        var address = o.DestinationAddress(currency.Network);
                        var amount = o.Value / currency.DigitsMultiplier;

                        var isSpent = o.IsSpent;

                        var tx = await DataRepository
                            .GetTransactionByIdAsync(Currency, o.TxId, currency.TransactionType)
                            .ConfigureAwait(false);

                        var isConfirmedOutput = tx?.IsConfirmed ?? false;

                        var isConfirmedInput = false;

                        if (isSpent)
                        {
                            var spentTx = await DataRepository
                                .GetTransactionByIdAsync(Currency, o.SpentTxPoint.Hash, currency.TransactionType)
                                .ConfigureAwait(false);

                            isConfirmedInput = spentTx?.IsConfirmed ?? false;
                        }

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
                catch (OperationCanceledException)
                {
                    Log.Debug($"{Currency} UpdateBalanceAsync canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, $"{Currency} UpdateBalanceAsync error.");
                }

            }, cancellationToken);
        }

        public override async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var currency = Config;

            var outputs = (await DataRepository
                .GetOutputsAsync(Currency, address, currency.OutputType())
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
                var amount = o.Value / (decimal) currency.DigitsMultiplier;

                var isSpent = o.IsSpent;

                //var isConfirmedOutput = unconfirmedTxs
                //    .FirstOrDefault(t => t.Outputs
                //        .FirstOrDefault(to => to.Index == o.Index && to.TxId == o.TxId) != null) == null;

                var isConfirmedOutput = (await DataRepository
                    .GetTransactionByIdAsync(Currency, o.TxId, currency.TransactionType)
                    .ConfigureAwait(false))
                    .IsConfirmed;

                //var isConfirmedInput = isSpent && unconfirmedTxs
                //    .FirstOrDefault(t => t.Inputs
                //        .FirstOrDefault(ti => ti.Index == o.Index && ti.Hash == o.TxId) != null) == null;

                var isConfirmedInput = isSpent && (await DataRepository
                    .GetTransactionByIdAsync(Currency, o.SpentTxPoint.Hash, currency.TransactionType)
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

        public virtual async Task<WalletAddress> GetFreeInternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var lastActiveAddress = await DataRepository
                .GetLastActiveWalletAddressAsync(
                    currency: Currency,
                    chain: Bip44.Internal,
                    keyType: CurrencyConfig.StandardKey)
                .ConfigureAwait(false);

            return await DivideAddressAsync(
                    account: Bip44.DefaultAccount,
                    chain: Bip44.Internal,
                    index: lastActiveAddress?.KeyIndex.Index + 1 ?? 0,
                    keyType: CurrencyConfig.StandardKey)
                .ConfigureAwait(false);
        }

        public async Task<WalletAddress> GetRefundAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var refundAddress = await GetFreeInternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            return ResolvePublicKey(refundAddress);
        }

        public async Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var redeemAddress = await GetFreeExternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            return ResolvePublicKey(redeemAddress);
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
            if (tx is not IBitcoinBasedTransaction btcBasedTx)
                throw new NotSupportedException("Transaction has incorrect type");

            await UpsertOutputsAsync(
                    tx: btcBasedTx,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var result = await ResolveTransactionTypeAsync(tx, cancellationToken)
                .ConfigureAwait(false);

            if (result == false)
                return;

            result = await DataRepository
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

        public Task<IBlockchainTransaction> GetTransactionByIdAsync(string txId)
        {
            var currency = Currencies.GetByName(Currency);

            return DataRepository.GetTransactionByIdAsync(
                currency: Currency,
                txId: txId,
                transactionType: currency.TransactionType);
        }

        #endregion Transactions

        #region Outputs

        public virtual async Task UpsertOutputsAsync(
            IEnumerable<ITxOutput> outputs,
            string address,
            bool notifyIfBalanceUpdated = true,
            CancellationToken cancellationToken = default)
        {
            await DataRepository
                .UpsertOutputsAsync(outputs, Currency, address)
                .ConfigureAwait(false);

            if (notifyIfBalanceUpdated)
                RaiseBalanceUpdated(new CurrencyEventArgs(Currency));
        }

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
                    address = output.DestinationAddress(Config.Network);
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

                await UpsertOutputAsync(Config, output, address)
                    .ConfigureAwait(false);
            }

            // update & save self inputs
            for (uint i = 0; i < tx.Inputs.Length; ++i)
            {
                var input = tx.Inputs[i];
                
                var selfInput = await DataRepository
                    .GetOutputAsync(Currency, input.Hash, input.Index, typeof(BitcoinBasedTxOutput))
                    .ConfigureAwait(false);

                if (selfInput == null)
                    continue;

                selfInput.SpentTxPoint = new TxPoint(i, tx.Id);

                await UpsertOutputAsync(Config, selfInput, selfInput.DestinationAddress(Config.Network))
                    .ConfigureAwait(false);
            }
        }

        private async Task UpsertOutputAsync(
            CurrencyConfig currency,
            ITxOutput output,
            string address)
        {
            var addressOutputs = (await DataRepository
                .GetOutputsAsync(currency.Name, address, currency.OutputType())
                .ConfigureAwait(false))
                .ToList();

            addressOutputs.Add(output);

            await DataRepository
                .UpsertOutputsAsync(
                    outputs: addressOutputs.RemoveDuplicates(),
                    currency: currency.Name,
                    address: address)
                .ConfigureAwait(false);
        }

        public Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync()
        {
            var currency = Config;

            return DataRepository.GetAvailableOutputsAsync(
                currency: Currency,
                outputType: currency.OutputType(),
                transactionType: currency.TransactionType);
        }

        public Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(string address)
        {
            var currency = Config;

            return DataRepository.GetAvailableOutputsAsync(
                currency: Currency,
                address: address,
                outputType: currency.OutputType(),
                transactionType: currency.TransactionType);
        }

        public Task<IEnumerable<ITxOutput>> GetOutputsAsync()
        {
            return DataRepository
                .GetOutputsAsync(Currency, Config.OutputType());
        }

        public Task<IEnumerable<ITxOutput>> GetOutputsAsync(string address)
        {
            return DataRepository
                .GetOutputsAsync(Currency, address, Config.OutputType());
        }

        public Task<ITxOutput> GetOutputAsync(string txId, uint index)
        {
            return DataRepository
                .GetOutputAsync(Currency, txId, index, Config.OutputType());
        }

        #endregion Outputs

        #region AddressResolver

        public Task<WalletAddress> GetAddressAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            return GetAddressAsync(address, cancellationToken);
        }

        #endregion AddressResolver
    }
}