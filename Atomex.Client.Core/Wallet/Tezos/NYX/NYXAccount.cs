﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomex.Wallet.Tezos
{
    public class NYXAccount : TezosAccount
    {
        public NYXAccount(
            string currency,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository)
                : base(currency, currencies, wallet, dataRepository)
        {
        }

        #region Common

        private TezosTokens.NYX NYX => Currencies.Get<TezosTokens.NYX>(Currency);
        private Atomex.Tezos Xtz => Currencies.Get<Atomex.Tezos>("XTZ");

        public override async Task<Error> SendAsync(
            IEnumerable<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool useDefaultFee = true,
            CancellationToken cancellationToken = default)
        {
            var nyx = NYX;

            var fromAddresses = from
                .Where(w => w.Address != to) // filter self address usage
                .ToList();

            var selectedAddresses = (await SelectUnspentAddressesAsync(
                    from: fromAddresses,
                    to: to,
                    amount: amount,
                    fee: fee,
                    feeUsagePolicy: useDefaultFee ? FeeUsagePolicy.EstimatedFee : FeeUsagePolicy.FeeForAllTransactions,
                    addressUsagePolicy: AddressUsagePolicy.UseMinimalBalanceFirst,
                    transactionType: BlockchainTransactionType.Output,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            if (!selectedAddresses.Any())
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: "Insufficient funds");

            // todo: min fee control
            var isFirstTx = true;

            foreach (var selectedAddress in selectedAddresses)
            {
                var addressAmountInDigits = selectedAddress.UsedAmount.ToTokenDigits(nyx.DigitsMultiplier);

                Log.Debug("Send {@amount} tokens from address {@address} with available balance {@balance}",
                    addressAmountInDigits,
                    selectedAddress.WalletAddress.Address,
                    selectedAddress.WalletAddress.AvailableBalance());

                var storageLimit = Math.Max(nyx.TransferStorageLimit - nyx.ActivationStorage, 0); // without activation storage fee

                var tx = new TezosTransaction
                {
                    Currency = nyx,
                    CreationTime = DateTime.UtcNow,
                    From = selectedAddress.WalletAddress.Address,
                    To = nyx.TokenContractAddress,
                    Fee = selectedAddress.UsedFee.ToMicroTez(),
                    GasLimit = nyx.TransferGasLimit,
                    StorageLimit = storageLimit,
                    Params = TransferParams(selectedAddress.WalletAddress.Address, to, Math.Round(addressAmountInDigits, 0)),
                    UseDefaultFee = useDefaultFee,
                    Type = BlockchainTransactionType.Output
                };

                var signResult = await Wallet
                    .SignAsync(tx, selectedAddress.WalletAddress, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                    return new Error(
                        code: Errors.TransactionSigningError,
                        description: "Transaction signing error");

                var broadcastResult = await nyx.BlockchainApi
                    .TryBroadcastAsync(tx, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (broadcastResult.HasError)
                    return broadcastResult.Error;

                var txId = broadcastResult.Value;

                if (txId == null)
                    return new Error(
                        code: Errors.TransactionBroadcastError,
                        description: "Transaction Id is null");

                Log.Debug("Transaction successfully sent with txId: {@id}", txId);

                tx.Amount = Math.Round(addressAmountInDigits, 0);

                await UpsertTransactionAsync(
                        tx: tx,
                        updateBalance: false,
                        notifyIfUnconfirmed: true,
                        notifyIfBalanceUpdated: false,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var xtzTx = tx.Clone();
                xtzTx.Currency = Xtz;
                xtzTx.Amount = 0;
                xtzTx.Type = BlockchainTransactionType.TokenCall;

                await UpsertTransactionAsync(
                        tx: xtzTx,
                        updateBalance: false,
                        notifyIfUnconfirmed: true,
                        notifyIfBalanceUpdated: false,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (isFirstTx)
                {
                    isFirstTx = false;
                }
            }

            UpdateBalanceAsync(cancellationToken)
                .FireAndForget();

            return null;
        }

        public override async Task<Error> SendAsync(
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool useDefaultFee = true,
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            return await SendAsync(
                    from: unspentAddresses,
                    to: to,
                    amount: amount,
                    fee: fee,
                    feePrice: feePrice,
                    useDefaultFee: useDefaultFee,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<decimal?> EstimateFeeAsync(
            string to,
            decimal amount,
            BlockchainTransactionType type,
            decimal fee = 0,
            decimal feePrice = 0,
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (!type.HasFlag(BlockchainTransactionType.SwapRedeem) &&
                !type.HasFlag(BlockchainTransactionType.SwapRefund))
            {
                unspentAddresses = unspentAddresses
                    .Where(w => w.Address != to)
                    .ToList();
            }

            if (!unspentAddresses.Any())
                return null; // insufficient funds

            var selectedAddresses = (await SelectUnspentAddressesAsync(
                    from: unspentAddresses,
                    to: to,
                    amount: amount,
                    fee: fee,
                    feeUsagePolicy: fee == 0 ? FeeUsagePolicy.EstimatedFee : FeeUsagePolicy.FeeForAllTransactions,
                    addressUsagePolicy: AddressUsagePolicy.UseMinimalBalanceFirst,
                    transactionType: type,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            if (!selectedAddresses.Any())
                return null; // insufficient funds

            return selectedAddresses.Sum(s => s.UsedFee);
        }

        public override async Task<(decimal, decimal, decimal)> EstimateMaxAmountToSendAsync(
            string to,
            BlockchainTransactionType type,
            decimal fee = 0,
            decimal feePrice = 0,
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            var xtz = Xtz;

            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (!type.HasFlag(BlockchainTransactionType.SwapRedeem) &&
                !type.HasFlag(BlockchainTransactionType.SwapRefund))
            {
                unspentAddresses = unspentAddresses
                    .Where(w => w.Address != to)
                    .ToList();
            }

            if (!unspentAddresses.Any())
                return (0m, 0m, 0m);

            // minimum balance first
            unspentAddresses = unspentAddresses
                .ToList()
                .SortList(new AvailableBalanceAscending());

            // use only max balance for swap payment
            if (type.HasFlag(BlockchainTransactionType.SwapPayment))
                unspentAddresses.RemoveRange(0, unspentAddresses.Count - 1);

            var amount = 0m;
            var feeAmount = 0m;

            var reserveFee = ReserveFee();

            foreach (var address in unspentAddresses)
            {
                var xtzAddress = await DataRepository
                    .GetWalletAddressAsync(xtz.Name, address.Address)
                    .ConfigureAwait(false);

                if (xtzAddress == null)
                    continue;

                var availableBalanceInTez = xtzAddress.AvailableBalance();

                var feeInTez = await FeeByType(
                        type: type,
                        from: address.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var storageFeeInTez = StorageFeeByTypeAsync(
                    type: type);

                availableBalanceInTez = availableBalanceInTez - feeInTez - storageFeeInTez - ((reserve && address == unspentAddresses.Last()) ? reserveFee : 0) - xtz.MicroTezReserve.ToTez();

                if (availableBalanceInTez < 0)
                    continue;

                amount += address.AvailableBalance();
                feeAmount += fee == 0 ? feeInTez : availableBalanceInTez + feeInTez;
            }

            return (amount, feeAmount, reserveFee);
        }

        protected override async Task<bool> ResolveTransactionTypeAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var nyx = NYX;

            if (!(tx is TezosTransaction xtzTx))
                throw new ArgumentException("Invalid tx type", nameof(tx));

            var oldTx = (TezosTransaction)await DataRepository
                .GetTransactionByIdAsync(Currency, tx.Id, nyx.TransactionType)
                .ConfigureAwait(false);

            //if (oldTx != null && oldTx.IsConfirmed)
            //  return false;

            var isFromSelf = await IsSelfAddressAsync(
                    address: xtzTx.From,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var isToSelf = await IsSelfAddressAsync(
                    address: xtzTx.To,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (isFromSelf)
                xtzTx.Type |= BlockchainTransactionType.Output;

            if (isToSelf)
                xtzTx.Type |= BlockchainTransactionType.Input;

            if (oldTx != null)
            {
                if (xtzTx.IsInternal)
                {
                    if (oldTx.Type.HasFlag(BlockchainTransactionType.SwapPayment))
                        xtzTx.Type |= BlockchainTransactionType.SwapPayment;
                    if (oldTx.Type.HasFlag(BlockchainTransactionType.SwapRedeem))
                        xtzTx.Type |= BlockchainTransactionType.SwapRedeem;
                    if (oldTx.Type.HasFlag(BlockchainTransactionType.SwapRefund))
                        xtzTx.Type |= BlockchainTransactionType.SwapRefund;
                }
                else
                    xtzTx.Type |= oldTx.Type;

                if (oldTx.IsConfirmed)
                {
                    xtzTx.Fee = oldTx.Fee;
                    xtzTx.GasLimit = oldTx.GasLimit;
                    xtzTx.GasUsed = oldTx.GasUsed;
                }
            }

            // todo: recognize swap payment/refund/redeem

            xtzTx.InternalTxs?.ForEach(async t => await ResolveTransactionTypeAsync(t, cancellationToken)
                .ConfigureAwait(false));

            ResolveTezosTxAlias(xtzTx);

            return true;
        }

        private async Task<decimal> FeeByType(
            BlockchainTransactionType type,
            string from,
            CancellationToken cancellationToken = default)
        {
            var nyx = NYX;

            var isRevealed = await IsRevealedSourceAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (type.HasFlag(BlockchainTransactionType.TokenApprove))
                return nyx.ApproveFee.ToTez();
            if (type.HasFlag(BlockchainTransactionType.SwapPayment))
                return nyx.ApproveFee.ToTez() + nyx.InitiateFee.ToTez() + (isRevealed ? 0 : nyx.RevealFee.ToTez());
            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return nyx.RefundFee.ToTez() + (isRevealed ? 0 : nyx.RevealFee.ToTez());
            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return nyx.RedeemFee.ToTez() + (isRevealed ? 0 : nyx.RevealFee.ToTez());

            return nyx.TransferFee.ToTez() + (isRevealed ? 0 : nyx.RevealFee.ToTez());
        }

        private decimal ReserveFee()
        {
            var xtz = Xtz;
            var nyx = NYX;

            return new[] {
                nyx.RedeemFee.ToTez() + Math.Max((nyx.RedeemStorageLimit - nyx.ActivationStorage) / nyx.StorageFeeMultiplier, 0),
                nyx.RefundFee.ToTez() + Math.Max((nyx.RefundStorageLimit - nyx.ActivationStorage) / nyx.StorageFeeMultiplier, 0),
                xtz.RedeemFee.ToTez() + Math.Max((xtz.RedeemStorageLimit - xtz.ActivationStorage) / xtz.StorageFeeMultiplier, 0),
                xtz.RefundFee.ToTez() + Math.Max((xtz.RefundStorageLimit - xtz.ActivationStorage) / xtz.StorageFeeMultiplier, 0)
            }.Max() + nyx.RevealFee.ToTez() + Xtz.MicroTezReserve.ToTez();
        }

        private decimal StorageFeeByTypeAsync(
            BlockchainTransactionType type)
        {
            var nyx = NYX;

            if (type.HasFlag(BlockchainTransactionType.TokenApprove))
                return nyx.ApproveStorageLimit;
            if (type.HasFlag(BlockchainTransactionType.SwapPayment))
                return (nyx.ApproveStorageLimit + nyx.InitiateStorageLimit) / nyx.StorageFeeMultiplier;
            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return (nyx.RefundStorageLimit - nyx.ActivationStorage) / nyx.StorageFeeMultiplier;
            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return (nyx.RedeemStorageLimit - nyx.ActivationStorage) / nyx.StorageFeeMultiplier;

            return (nyx.TransferStorageLimit - nyx.ActivationStorage) / nyx.StorageFeeMultiplier;
        }

        #endregion Common

        #region Balances

        public override async Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            var nyx = NYX;

            var txs = (await DataRepository
                .GetTransactionsAsync(Currency, nyx.TransactionType)
                .ConfigureAwait(false))
                .Cast<TezosTransaction>()
                .ToList();

            var internalTxs = txs.Aggregate(new List<TezosTransaction>(), (list, tx) =>
            {
                if (tx.InternalTxs != null)
                    list.AddRange(tx.InternalTxs);

                return list;
            });

            // calculate balances
            var totalBalanceSum = 0m;
            var totalUnconfirmedIncome = 0m;
            var totalUnconfirmedOutcome = 0m;

            var addresses = new Dictionary<string, WalletAddress>();

            foreach (var tx in txs.Concat(internalTxs))
            {
                var selfAddresses = new HashSet<string>();

                var isFromSelf = await IsSelfAddressAsync(tx.From, cancellationToken)
                    .ConfigureAwait(false);

                //if (tx.Type.HasFlag(BlockchainTransactionType.Output))
                if (isFromSelf)
                    selfAddresses.Add(tx.From);

                var isToSelf = await IsSelfAddressAsync(tx.To, cancellationToken)
                    .ConfigureAwait(false);

                //if (tx.Type.HasFlag(BlockchainTransactionType.Input))
                if (isToSelf)
                    selfAddresses.Add(tx.To);

                foreach (var address in selfAddresses)
                {
                    var isIncome = address == tx.To;
                    var isOutcome = address == tx.From;
                    var isConfirmed = tx.IsConfirmed;
                    var isFailed = tx.State == BlockchainTransactionState.Failed;

                    var income = isIncome && !isFailed
                        ? tx.Amount.FromTokenDigits(nyx.DigitsMultiplier)
                        : 0;

                    var outcome = isOutcome && !isFailed
                        ? -tx.Amount.FromTokenDigits(nyx.DigitsMultiplier)
                        : 0;

                    if (addresses.TryGetValue(address, out var walletAddress))
                    {
                        walletAddress.Balance += isConfirmed ? income + outcome : 0;
                        walletAddress.UnconfirmedIncome += !isConfirmed ? income : 0;
                        walletAddress.UnconfirmedOutcome += !isConfirmed ? outcome : 0;
                    }
                    else
                    {
                        walletAddress = await DataRepository
                            .GetWalletAddressAsync(Currency, address)
                            .ConfigureAwait(false);

                        if (walletAddress == null)
                            continue;

                        walletAddress.Balance = isConfirmed ? income + outcome : 0;
                        walletAddress.UnconfirmedIncome = !isConfirmed ? income : 0;
                        walletAddress.UnconfirmedOutcome = !isConfirmed ? outcome : 0;
                        walletAddress.HasActivity = true;

                        addresses.Add(address, walletAddress);
                    }

                    totalBalanceSum += isConfirmed ? income + outcome : 0;
                    totalUnconfirmedIncome += !isConfirmed ? income : 0;
                    totalUnconfirmedOutcome += !isConfirmed ? outcome : 0;
                }
            }

            Balance = totalBalanceSum;

            var totalBalance = 0m;

            foreach (var wa in addresses.Values)
            {
                var nyxApi = nyx.BlockchainApi as ITokenBlockchainApi;

                var balanceResult = await nyxApi
                    .TryGetTokenBigMapBalanceAsync(
                        address: wa.Address,
                        pointer: nyx.TokenPointerBalance,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (balanceResult.HasError)
                {
                    Log.Error("Error while getting token balance for {@address} with code {@code} and description {@description}",
                        wa.Address,
                        balanceResult.Error.Code,
                        balanceResult.Error.Description);

                    continue; // todo: may be return?
                }

                wa.Balance = balanceResult.Value.FromTokenDigits(nyx.DigitsMultiplier);

                totalBalance += wa.Balance;
            }

            if (totalBalanceSum != totalBalance)
            {
                Log.Warning("Transaction balance sum is different from the actual {@name} token balance",
                    nyx.Name);

                Balance = totalBalance;
            }

            // upsert addresses
            await DataRepository
                .UpsertAddressesAsync(addresses.Values)
                .ConfigureAwait(false);

            UnconfirmedIncome = totalUnconfirmedIncome;
            UnconfirmedOutcome = totalUnconfirmedOutcome;

            RaiseBalanceUpdated(new CurrencyEventArgs(Currency));
        }

        public override async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var nyx = NYX;

            var walletAddress = await DataRepository
                .GetWalletAddressAsync(Currency, address)
                .ConfigureAwait(false);

            if (walletAddress == null)
                return;

            var txs = (await DataRepository
                .GetTransactionsAsync(Currency, nyx.TransactionType)
                .ConfigureAwait(false))
                .Cast<TezosTransaction>()
                .ToList();

            var internalTxs = txs.Aggregate(new List<TezosTransaction>(), (list, tx) =>
            {
                if (tx.InternalTxs != null)
                    list.AddRange(tx.InternalTxs);

                return list;
            });

            var balance = 0m;
            var unconfirmedIncome = 0m;
            var unconfirmedOutcome = 0m;

            foreach (var tx in txs.Concat(internalTxs))
            {
                var isIncome = address == tx.To;
                var isOutcome = address == tx.From;
                var isConfirmed = tx.IsConfirmed;
                var isFailed = tx.State == BlockchainTransactionState.Failed;

                var income = isIncome && !isFailed
                    ? tx.Amount.FromTokenDigits(nyx.DigitsMultiplier)
                    : 0;

                var outcome = isOutcome && !isFailed
                    ? -tx.Amount.FromTokenDigits(nyx.DigitsMultiplier)
                    : 0;

                balance += isConfirmed ? income + outcome : 0;
                unconfirmedIncome += !isConfirmed ? income : 0;
                unconfirmedOutcome += !isConfirmed ? outcome : 0;
            }

            var nyxApi = nyx.BlockchainApi as ITokenBlockchainApi;

            var balanceResult = await nyxApi
                .TryGetTokenBigMapBalanceAsync(
                    address: address,
                    pointer: nyx.TokenPointerBalance,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (balanceResult.HasError)
            {
                Log.Error("Error while balance update token for {@address} with code {@code} and description {@description}",
                    address,
                    balanceResult.Error.Code,
                    balanceResult.Error.Description);
                return;
            }

            var balanceRes = balanceResult.Value.FromTokenDigits(nyx.DigitsMultiplier);

            if (balance != balanceRes)
            {
                Log.Warning("Transaction balance sum for address {@address} is {@balanceSum}, which is different from the actual address balance {@balance}",
                    address,
                    balance,
                    balanceRes);

                balance = balanceRes;
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

        public override async Task<WalletAddress> GetRedeemAddressAsync(   //todo: match it with xtz balances
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return ResolvePublicKey(unspentAddresses.MaxBy(w => w.AvailableBalance()));

            var unspentTezosAddresses = await DataRepository
                .GetUnspentAddressesAsync("XTZ")
                .ConfigureAwait(false);

            if (unspentTezosAddresses.Any())
            {
                var tezosAddress = unspentTezosAddresses.MaxBy(a => a.AvailableBalance());

                return await DivideAddressAsync(
                    chain: tezosAddress.KeyIndex.Chain,
                    index: tezosAddress.KeyIndex.Index,
                    cancellationToken: cancellationToken);
            }

            foreach (var chain in new[] { Bip44.Internal, Bip44.External })
            {
                var lastActiveAddress = await DataRepository
                    .GetLastActiveWalletAddressAsync(
                        currency: Currency,
                        chain: chain)
                    .ConfigureAwait(false);

                if (lastActiveAddress != null)
                    return ResolvePublicKey(lastActiveAddress);
            }

            return await base.GetRedeemAddressAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string toAddress,
            decimal amount,
            decimal fee,
            decimal feePrice,
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = (await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false))
                .ToList();

            if (!transactionType.HasFlag(BlockchainTransactionType.SwapRedeem) &&
                !transactionType.HasFlag(BlockchainTransactionType.SwapRefund))
            {
                unspentAddresses = unspentAddresses
                    .Where(w => w.Address != toAddress)
                    .ToList();
            }

            var selectedAddresses = await SelectUnspentAddressesAsync(
                    from: unspentAddresses,
                    to: toAddress,
                    amount: amount,
                    fee: fee,
                    feeUsagePolicy: feeUsagePolicy,
                    addressUsagePolicy: addressUsagePolicy,
                    transactionType: transactionType,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return ResolvePublicKeys(selectedAddresses
                .Select(w => w.WalletAddress)
                .ToList());
        }

        private async Task<IEnumerable<SelectedWalletAddress>> SelectUnspentAddressesAsync(
            IList<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default)
        {
            var xtz = Xtz;

            if (addressUsagePolicy == AddressUsagePolicy.UseMinimalBalanceFirst)
            {
                from = from.ToList().SortList(new AvailableBalanceAscending());
            }
            else if (addressUsagePolicy == AddressUsagePolicy.UseMaximumBalanceFirst)
            {
                from = from.ToList().SortList(new AvailableBalanceDescending());
            }
            else if (addressUsagePolicy == AddressUsagePolicy.UseMaximumChainBalanceFirst)
            {
                var xtzUnspentAddresses = (await DataRepository
                    .GetUnspentAddressesAsync(xtz.Name)
                    .ConfigureAwait(false))
                    .ToList();

                if (!xtzUnspentAddresses.Any())
                {
                    Log.Debug("Unsufficient XTZ ammount for NYX token processing");
                    return Enumerable.Empty<SelectedWalletAddress>();
                }

                xtzUnspentAddresses = xtzUnspentAddresses.ToList().SortList(new AvailableBalanceDescending());

                from = xtzUnspentAddresses.FindAll(a => from.Select(b => b.Address).ToList().Contains(a.Address));
            }
            else if (addressUsagePolicy == AddressUsagePolicy.UseOnlyOneAddress)
            {
                var xtzUnspentAddresses = (await DataRepository
                    .GetUnspentAddressesAsync(xtz.Name)
                    .ConfigureAwait(false))
                    .ToList();

                xtzUnspentAddresses = xtzUnspentAddresses.ToList().SortList(new AvailableBalanceAscending());

                if (!xtzUnspentAddresses.Any())
                {
                    Log.Debug("Unsufficient XTZ ammount for NYX token processing");
                    return Enumerable.Empty<SelectedWalletAddress>();
                }

                from = from.ToList()
                    .Concat(xtzUnspentAddresses.FindAll(a => from.Select(b => b.Address).ToList().Contains(a.Address) == false))
                    .ToList();

                //"to" used for redeem and refund - using the destination address is prefered
                from = from.Where(a => a.Address == to).Concat(from.Where(a => a.Address != to)).ToList();
            }

            var result = new List<SelectedWalletAddress>();
            var requiredAmount = amount;

            var isFirstTx = true;
            var completed = false;

            foreach (var address in from)
            {
                var availableBalanceInTokens = address.AvailableBalance();

                var xtzAddress = await DataRepository
                    .GetWalletAddressAsync(xtz.Name, address.Address)
                    .ConfigureAwait(false);

                var availableBalanceInTez = xtzAddress != null ? xtzAddress.AvailableBalance() : 0m;

                var txFeeInTez = feeUsagePolicy == FeeUsagePolicy.FeePerTransaction
                    ? fee
                    : await FeeByType(
                            type: transactionType,
                            from: address.Address,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                var storageFeeInTez = StorageFeeByTypeAsync(
                    type: transactionType);

                var leftBalanceInTez = availableBalanceInTez - txFeeInTez - storageFeeInTez - xtz.MicroTezReserve.ToTez();

                if (leftBalanceInTez < 0) // ignore address with balance less than fee
                {
                    Log.Debug("Unsufficient XTZ ammount for NYX token processing on address {@address} with available balance {@balance} and needed amount {@amount}",
                        address.Address,
                        availableBalanceInTez,
                        txFeeInTez + storageFeeInTez + xtz.MicroTezReserve.ToTez());
                    continue;
                }

                if (addressUsagePolicy == AddressUsagePolicy.UseOnlyOneAddress)
                {
                    if (Math.Min(availableBalanceInTokens, requiredAmount) == requiredAmount)
                        return new List<SelectedWalletAddress> {
                            new SelectedWalletAddress
                            {
                                WalletAddress = address,
                                UsedAmount = amount,
                                UsedFee = txFeeInTez,
                                UsedStorageFee = storageFeeInTez
                            }
                        };
                    continue;
                }

                decimal amountToUse = 0;

                amountToUse = Math.Min(availableBalanceInTokens, requiredAmount);
                requiredAmount -= amountToUse;

                if (amountToUse > 0)
                    result.Add(new SelectedWalletAddress
                    {
                        WalletAddress = address,
                        UsedAmount = amountToUse,
                        UsedFee = txFeeInTez,
                        UsedStorageFee = storageFeeInTez
                    });

                if (requiredAmount <= 0)
                {
                    completed = true;

                    if (feeUsagePolicy == FeeUsagePolicy.FeeForAllTransactions)
                    {
                        var estimatedFee = result.Sum(s => s.UsedFee);
                        var remainingFee = fee - estimatedFee;

                        var extraFee = 0m;

                        if (remainingFee > 0)
                        {
                            foreach (var s in result)
                            {
                                xtzAddress = await DataRepository
                                    .GetWalletAddressAsync(xtz.Name, s.WalletAddress.Address)
                                    .ConfigureAwait(false);

                                extraFee = s == result.Last()
                                    ? Math.Min(xtzAddress.AvailableBalance() - s.UsedFee - s.UsedStorageFee - xtz.MicroTezReserve.ToTez(), remainingFee)
                                    : Math.Min(xtzAddress.AvailableBalance() - s.UsedFee - s.UsedStorageFee - xtz.MicroTezReserve.ToTez(), Math.Round(remainingFee * s.UsedFee / estimatedFee, xtz.Digits));

                                remainingFee -= extraFee;
                                estimatedFee -= s.UsedFee;
                                s.UsedFee += extraFee;

                                if (remainingFee <= 0)
                                    break;
                            }
                        }
                        else if (remainingFee < 0) //todo: delete
                        {
                            Log.Error("Error fee is too small for transaction, fee is {@fee} with estimated fee {@estimatedFee}",
                                fee,
                                estimatedFee);
                            return Enumerable.Empty<SelectedWalletAddress>();
                        }
                    }

                    break;
                }
                if (isFirstTx)
                    isFirstTx = false;
            }
            if (completed)
                return result;

            if (feeUsagePolicy == FeeUsagePolicy.FeeForAllTransactions) //todo: delete
            {
                Log.Error("Error fee is too big for transaction, fee is {@fee} with estimated fee {@estimatedFee}",
                    fee,
                    result.Sum(s => s.UsedFee));
            }

            return Enumerable.Empty<SelectedWalletAddress>();
        }

        public override async Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = await DataRepository
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return unspentAddresses.MaxBy(a => a.AvailableBalance());

            var unspentTezosAddresses = await DataRepository
                .GetUnspentAddressesAsync("XTZ")
                .ConfigureAwait(false);

            if (unspentTezosAddresses.Any())
            {
                var tezosAddress = unspentTezosAddresses.MaxBy(a => a.AvailableBalance());

                return await DivideAddressAsync(
                    chain: tezosAddress.KeyIndex.Chain,
                    index: tezosAddress.KeyIndex.Index,
                    cancellationToken: cancellationToken);
            }

            var lastActiveAddress = await DataRepository
                .GetLastActiveWalletAddressAsync(
                    currency: "XTZ",
                    chain: Bip44.External)
                .ConfigureAwait(false);

            return await DivideAddressAsync(
                    chain: Bip44.External,
                    index: lastActiveAddress?.KeyIndex.Index + 1 ?? 0,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion Addresses

        #region Helpers
        private JObject TransferParams(string from, string to, decimal amount)
        {
            return JObject.Parse(@"{'entrypoint':'transfer','value':[{'prim':'Pair','args':[{'int':'" + amount + "'},{'string':'" + to + "'}]}]}");
        }

        #endregion Helpers
    }
}