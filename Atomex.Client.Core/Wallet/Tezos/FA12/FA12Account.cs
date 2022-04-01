using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.TezosTokens;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet.Tezos
{
    public class Fa12Account : TezosTokenAccount, ILegacyCurrencyAccount
    {
        private Fa12Config Fa12Config => Currencies.Get<Fa12Config>(Currency);

        public Fa12Account(
            string currency,
            string tokenContract,
            decimal tokenId,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository,
            TezosAccount tezosAccount)
            : base(currency,
                  "FA12",
                  tokenContract,
                  tokenId,
                  currencies,
                  wallet,
                  dataRepository,
                  tezosAccount)
        {
        }

        #region Common

        public async Task<Error> SendAsync(
            IEnumerable<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool useDefaultFee = true,
            CancellationToken cancellationToken = default)
        {
            var fa12Config = Fa12Config;
            var xtzConfig = XtzConfig;

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

            foreach (var selectedAddress in selectedAddresses)
            {
                var digitsMultiplier = fa12Config.DigitsMultiplier != 0
                    ? fa12Config.DigitsMultiplier
                    : (decimal)Math.Pow(10, selectedAddress.WalletAddress.TokenBalance.Decimals);

                var addressAmountInDigits = selectedAddress.UsedAmount.ToTokenDigits(digitsMultiplier);

                Log.Debug("Send {@amount} tokens from address {@address} with available balance {@balance}",
                    addressAmountInDigits,
                    selectedAddress.WalletAddress.Address,
                    selectedAddress.WalletAddress.AvailableBalance());

                var storageLimit = Math.Max(fa12Config.TransferStorageLimit - fa12Config.ActivationStorage, 0); // without activation storage fee
               
                var tx = new TezosTransaction
                {
                    Currency      = xtzConfig.Name,
                    CreationTime  = DateTime.UtcNow,
                    From          = selectedAddress.WalletAddress.Address,
                    To            = _tokenContract,
                    Fee           = selectedAddress.UsedFee.ToMicroTez(),
                    GasLimit      = fa12Config.TransferGasLimit,
                    StorageLimit  = storageLimit,
                    Params        = CreateTransferParams(selectedAddress.WalletAddress.Address, to, Math.Round(addressAmountInDigits, 0)),
                    Type          = BlockchainTransactionType.Output | BlockchainTransactionType.TokenCall,

                    UseRun              = useDefaultFee,
                    UseSafeStorageLimit = true,
                    UseOfflineCounter   = true
                };

                try
                {
                    await _tezosAccount.AddressLocker
                        .LockAsync(selectedAddress.WalletAddress.Address, cancellationToken)
                        .ConfigureAwait(false);

                    // temporary fix: check operation sequence
                    await TezosOperationsSequencer
                        .WaitAsync(selectedAddress.WalletAddress.Address, _tezosAccount, cancellationToken)
                        .ConfigureAwait(false);

                    using var securePublicKey = Wallet.GetPublicKey(
                        currency: xtzConfig,
                        keyIndex: selectedAddress.WalletAddress.KeyIndex,
                        keyType: selectedAddress.WalletAddress.KeyType);

                    // fill operation
                    var (fillResult, isRunSuccess, hasReveal) = await tx
                        .FillOperationsAsync(
                            securePublicKey: securePublicKey,
                            tezosConfig: xtzConfig,
                            headOffset: TezosConfig.HeadOffset,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    var signResult = await Wallet
                        .SignAsync(tx, selectedAddress.WalletAddress, xtzConfig, cancellationToken)
                        .ConfigureAwait(false);

                    if (!signResult)
                        return new Error(
                            code: Errors.TransactionSigningError,
                            description: "Transaction signing error");

                    var broadcastResult = await XtzConfig.BlockchainApi
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

                    await _tezosAccount
                        .UpsertTransactionAsync(
                            tx: tx,
                            updateBalance: false,
                            notifyIfUnconfirmed: true,
                            notifyIfBalanceUpdated: false,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _tezosAccount.AddressLocker
                        .Unlock(selectedAddress.WalletAddress.Address);
                }
            }

            return null;
        }

        public async Task<Error> SendAsync(
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool useDefaultFee = true,
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = (await DataRepository
                .GetUnspentTezosTokenAddressesAsync(TokenType, _tokenContract, _tokenId)
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

        public async Task<decimal?> EstimateFeeAsync(
            string to,
            decimal amount,
            BlockchainTransactionType type,
            decimal fee = 0,
            decimal feePrice = 0,
            CancellationToken cancellationToken = default)
        {
            var unspentAddresses = (await DataRepository
                .GetUnspentTezosTokenAddressesAsync(TokenType, _tokenContract, _tokenId)
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

        public override async Task<(decimal fee, bool isEnougth)> EstimateTransferFeeAsync(
            string from,
            CancellationToken cancellationToken = default)
        {
            var xtzAddress = await _tezosAccount
                .GetAddressAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var txFeeInTez = await FeeByType(
                    type: BlockchainTransactionType.Output,
                    from: xtzAddress?.Address,
                    isFirstTx: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInTez = StorageFeeByTypeAsync(
                type: BlockchainTransactionType.Output,
                isFirstTx: true);

            var requiredFeeInTez = txFeeInTez + storageFeeInTez + XtzConfig.MicroTezReserve.ToTez();

            var availableBalanceInTez = xtzAddress != null
                ? xtzAddress.AvailableBalance()
                : 0m;

            return (
                fee: requiredFeeInTez,
                isEnougth: availableBalanceInTez >= requiredFeeInTez);
        }

        public async Task<(decimal, decimal, decimal)> EstimateMaxAmountToSendAsync(
            string to,
            BlockchainTransactionType type,
            decimal fee = 0,
            decimal feePrice = 0,
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            var xtz = XtzConfig;

            var unspentAddresses = (await DataRepository
                .GetUnspentTezosTokenAddressesAsync(TokenType, _tokenContract, _tokenId)
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

            var isFirstTx = true;
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
                        isFirstTx: isFirstTx,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var storageFeeInTez = StorageFeeByTypeAsync(
                    type: type,
                    isFirstTx: isFirstTx);

                availableBalanceInTez = availableBalanceInTez - feeInTez - storageFeeInTez - ((reserve && address == unspentAddresses.Last()) ? reserveFee : 0) - xtz.MicroTezReserve.ToTez();

                if (availableBalanceInTez < 0)
                    continue;

                amount += address.AvailableBalance();
                feeAmount += fee == 0 ? feeInTez : availableBalanceInTez + feeInTez;

                if (isFirstTx)
                    isFirstTx = false;
            }

            return (amount, feeAmount, reserveFee);
        }

        private async Task<decimal> FeeByType(
            BlockchainTransactionType type,
            string from,
            bool isFirstTx,
            CancellationToken cancellationToken = default)
        {
            var fa12 = Fa12Config;

            var isRevealed = from != null && await _tezosAccount
                .IsRevealedSourceAsync(from, cancellationToken)
                .ConfigureAwait(false);

            if (type.HasFlag(BlockchainTransactionType.TokenApprove))
                return fa12.ApproveFee.ToTez();
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && isFirstTx)
                return fa12.ApproveFee.ToTez() * 2 + fa12.InitiateFee.ToTez() + (isRevealed ? 0 : fa12.RevealFee.ToTez());
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && !isFirstTx)
                return fa12.ApproveFee.ToTez() * 2 + fa12.AddFee.ToTez() + (isRevealed ? 0 : fa12.RevealFee.ToTez());
            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return fa12.RefundFee.ToTez() + (isRevealed ? 0 : fa12.RevealFee.ToTez());
            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return fa12.RedeemFee.ToTez() + (isRevealed ? 0 : fa12.RevealFee.ToTez());

            return fa12.TransferFee.ToTez() + (isRevealed ? 0 : fa12.RevealFee.ToTez());
        }

        private decimal ReserveFee()
        {
            var xtz = XtzConfig;
            var fa12 = Fa12Config;

            return new[]
            {
                (fa12.RedeemFee + Math.Max((fa12.RedeemStorageLimit - fa12.ActivationStorage) * fa12.StorageFeeMultiplier, 0)).ToTez(),
                (fa12.RefundFee + Math.Max((fa12.RefundStorageLimit - fa12.ActivationStorage) * fa12.StorageFeeMultiplier, 0)).ToTez(),
                (xtz.RedeemFee + Math.Max((xtz.RedeemStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0)).ToTez(),
                (xtz.RefundFee + Math.Max((xtz.RefundStorageLimit - xtz.ActivationStorage) * xtz.StorageFeeMultiplier, 0)).ToTez()

            }.Max() + fa12.RevealFee.ToTez() + XtzConfig.MicroTezReserve.ToTez();
        }

        private decimal StorageFeeByTypeAsync(
            BlockchainTransactionType type,
            bool isFirstTx)
        {
            var fa12 = Fa12Config;

            if (type.HasFlag(BlockchainTransactionType.TokenApprove))
                return fa12.ApproveStorageLimit.ToTez();
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && isFirstTx)
                return ((fa12.ApproveStorageLimit * 2 + fa12.InitiateStorageLimit) * fa12.StorageFeeMultiplier).ToTez();
            if (type.HasFlag(BlockchainTransactionType.SwapPayment) && !isFirstTx)
                return ((fa12.ApproveStorageLimit * 2 + fa12.AddStorageLimit) * fa12.StorageFeeMultiplier).ToTez();
            if (type.HasFlag(BlockchainTransactionType.SwapRefund))
                return ((fa12.RefundStorageLimit - fa12.ActivationStorage) * fa12.StorageFeeMultiplier).ToTez();
            if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
                return ((fa12.RedeemStorageLimit - fa12.ActivationStorage) * fa12.StorageFeeMultiplier).ToTez();

            return ((fa12.TransferStorageLimit - fa12.ActivationStorage) * fa12.StorageFeeMultiplier).ToTez();
        }

        #endregion Common

        #region Addresses

        public Task<WalletAddress> GetRedeemAddressAsync( // todo: match it with xtz balances
            CancellationToken cancellationToken = default)
        {
            return GetFreeExternalAddressAsync(cancellationToken);
        }

        public async Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
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
                .GetUnspentTezosTokenAddressesAsync(TokenType, _tokenContract, _tokenId)
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

            return selectedAddresses
                .Select(w => w.WalletAddress.ResolvePublicKey(Currencies, Wallet))
                .ToList();
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
            var xtz = XtzConfig;

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
                    .GetUnspentAddressesAsync(TezosConfig.Xtz)
                    .ConfigureAwait(false))
                    .ToList();

                if (!xtzUnspentAddresses.Any())
                {
                    Log.Debug("Unsufficient XTZ ammount for FA12 token processing");
                    return Enumerable.Empty<SelectedWalletAddress>();
                }

                xtzUnspentAddresses = xtzUnspentAddresses.ToList().SortList(new AvailableBalanceDescending());

                from = xtzUnspentAddresses.FindAll(a => from.Select(b => b.Address).ToList().Contains(a.Address));
            }
            else if (addressUsagePolicy == AddressUsagePolicy.UseOnlyOneAddress)
            {
                var xtzUnspentAddresses = (await DataRepository
                    .GetUnspentAddressesAsync(TezosConfig.Xtz)
                    .ConfigureAwait(false))
                    .ToList();

                xtzUnspentAddresses = xtzUnspentAddresses.ToList().SortList(new AvailableBalanceAscending());

                if (!xtzUnspentAddresses.Any())
                {
                    Log.Debug("Unsufficient XTZ ammount for FA12 token processing");
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
                            isFirstTx: isFirstTx,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                var storageFeeInTez = StorageFeeByTypeAsync(
                    type: transactionType,
                    isFirstTx: isFirstTx);

                var leftBalanceInTez = availableBalanceInTez - txFeeInTez - storageFeeInTez - xtz.MicroTezReserve.ToTez();
        
                if (leftBalanceInTez < 0) // ignore address with balance less than fee
                {
                    Log.Debug("Unsufficient XTZ ammount for FA12 token processing on address {@address} with available balance {@balance} and needed amount {@amount}",
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
                                WalletAddress  = address,
                                UsedAmount     = amount,
                                UsedFee        = txFeeInTez,
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
                        WalletAddress  = address,
                        UsedAmount     = amountToUse,
                        UsedFee        = txFeeInTez,
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

        public Task<IEnumerable<WalletAddress>> GetUnspentTokenAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return DataRepository
                .GetUnspentTezosTokenAddressesAsync(TokenType, _tokenContract, _tokenId);
        }

        #endregion Addresses

        #region Transactions

        public Task UpsertTransactionAsync(
            IBlockchainTransaction tx,
            bool updateBalance = false,
            bool notifyIfUnconfirmed = true,
            bool notifyIfBalanceUpdated = true,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        #endregion Transactions

        #region Helpers

        private JObject CreateTransferParams(string from, string to, decimal amount)
        {
            return JObject.FromObject(new
            {
                entrypoint = "transfer",
                value = new
                {
                    prim = "Pair",
                    args = new object[]
                    {
                        new
                        {
                            @string = from
                        },
                        new
                        {
                            prim = "Pair",
                            args = new object[]
                            {
                                new
                                {
                                    @string = to
                                },
                                new
                                {
                                    @int = amount.ToString()
                                }
                            }
                        }
                    }
                }
            });
        }

        #endregion Helpers
    }
}