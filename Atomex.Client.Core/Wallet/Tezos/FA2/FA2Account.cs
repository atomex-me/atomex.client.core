using System;
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
using Atomex.Wallet.Bip;

namespace Atomex.Wallet.Tezos
{
    public class Fa2Account : CurrencyAccount
    {
        private readonly TezosAccount _tezosAccount;
        private Fa2Config Fa2Config => Currencies.Get<Fa2Config>(Currency);
        private TezosConfig XtzConfig => Currencies.Get<TezosConfig>("XTZ");

        public Fa2Account(
            string currency,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository,
            TezosAccount tezosAccount)
                : base(currency, currencies, wallet, dataRepository)
        {
            _tezosAccount = tezosAccount;
        }

        #region Common

        public async Task<Error> SendAsync(
            string from,
            string to,
            int amount,
            string tokenContract,
            int tokenId,
            int fee,
            bool useDefaultFee = true,
            CancellationToken cancellationToken = default)
        {
            var fa2 = Fa2Config;
            var xtz = XtzConfig;

            var fa2token = Fa2Config.UniqueTokenId(tokenContract, tokenId);

            var fromAddress = await DataRepository
                .GetWalletAddressAsync(fa2token, from)
                .ConfigureAwait(false);

            if (fromAddress.AvailableBalance() < amount)
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: $"Insufficient tokens. " +
                        $"Available: {fromAddress.AvailableBalance()}. " +
                        $"Required: {amount}.");

            var xtzAddress = await DataRepository
                .GetWalletAddressAsync(xtz.Name, from)
                .ConfigureAwait(false);

            var isRevealed = await _tezosAccount
                .IsRevealedSourceAsync(from, cancellationToken)
                .ConfigureAwait(false);

            var storageFeeInMtz = (fa2.TransferStorageLimit - fa2.ActivationStorage) * fa2.StorageFeeMultiplier;

            var feeInMtz = useDefaultFee
                ? fa2.TransferFee + (isRevealed ? 0 : fa2.RevealFee) + storageFeeInMtz
                : fee;

            var availableBalanceInTz = xtzAddress.AvailableBalance().ToMicroTez() - feeInMtz - xtz.MicroTezReserve;

            if (availableBalanceInTz < 0)
            {
                return new Error(
                    code: Errors.InsufficientFunds,
                    description: $"Insufficient funds to pay fee for address {from}. " +
                        $"Available: {xtzAddress.AvailableBalance()}. " +
                        $"Required: {feeInMtz + xtz.MicroTezReserve}");
            }

            Log.Debug("Send {@amount} tokens from address {@address} with available balance {@balance}",
                amount,
                from,
                fromAddress.AvailableBalance());

            var storageLimit = Math.Max(fa2.TransferStorageLimit - fa2.ActivationStorage, 0); // without activation storage fee

            var tx = new TezosTransaction
            {
                Currency     = xtz,
                CreationTime = DateTime.UtcNow,
                From         = from,
                To           = tokenContract,
                Fee          = feeInMtz,
                GasLimit     = fa2.TransferGasLimit,
                StorageLimit = storageLimit,
                Params       = TransferParams(tokenId, from, to, amount),
                Type         = BlockchainTransactionType.Output | BlockchainTransactionType.TokenCall,

                UseRun              = useDefaultFee,
                UseSafeStorageLimit = true,
                UseOfflineCounter   = true
            };

            try
            {
                await _tezosAccount.AddressLocker
                    .LockAsync(from, cancellationToken)
                    .ConfigureAwait(false);

                using var securePublicKey = Wallet
                    .GetPublicKey(fa2, fromAddress.KeyIndex);

                // fill operation
                var fillResult = await tx
                    .FillOperationsAsync(
                        securePublicKey: securePublicKey,
                        headOffset: TezosConfig.HeadOffset,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var signResult = await Wallet
                    .SignAsync(tx, fromAddress, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                    return new Error(
                        code: Errors.TransactionSigningError,
                        description: "Transaction signing error");

                var broadcastResult = await fa2.BlockchainApi
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

                await UpsertTransactionAsync(
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
                    .Unlock(from);
            }

            _ = UpdateBalanceAsync(cancellationToken);

            return null;
        }

        protected override async Task<bool> ResolveTransactionTypeAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var fa2 = Fa2Config;

            if (!(tx is TezosTransaction xtzTx))
                throw new ArgumentException("Invalid tx type", nameof(tx));

            var oldTx = (TezosTransaction)await DataRepository
                .GetTransactionByIdAsync(Currency, tx.Id, fa2.TransactionType)
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

            TezosAccount.ResolveTezosTxAlias(xtzTx);

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
                    //var fa2 = Fa2Config;

                    //foreach (var wa in addresses.Values)
                    //{
                    //    var fa2Api = fa2.BlockchainApi as ITokenBlockchainApi;

                    //    var balanceResult = await fa2Api
                    //        .TryGetTokenBigMapBalanceAsync(
                    //            address: wa.Address,
                    //            pointer: fa2.TokenPointerBalance,
                    //            cancellationToken: cancellationToken)
                    //        .ConfigureAwait(false);

                    //    if (balanceResult.HasError)
                    //    {
                    //        Log.Error("Error while getting token balance for {@address} with code {@code} and description {@description}",
                    //            wa.Address,
                    //            balanceResult.Error.Code,
                    //            balanceResult.Error.Description);

                    //        continue; // todo: may be return?
                    //    }

                    //    wa.Balance = balanceResult.Value.FromTokenDigits(fa2.DigitsMultiplier);

                    //    totalBalance += wa.Balance;
                    //}

                    //if (totalBalanceSum != totalBalance)
                    //{
                    //    Log.Warning("Transaction balance sum is different from the actual {@name} token balance",
                    //        fa2.Name);

                    //    //Balance = totalBalance;
                    //}

                    //// upsert addresses
                    //await DataRepository
                    //    .UpsertAddressesAsync(addresses.Values)
                    //    .ConfigureAwait(false);

                    //UnconfirmedIncome = totalUnconfirmedIncome;
                    //UnconfirmedOutcome = totalUnconfirmedOutcome;

                    //RaiseBalanceUpdated(new CurrencyEventArgs(Currency));
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
            //var fa2 = Fa2Config;

            //var walletAddress = await DataRepository
            //    .GetWalletAddressAsync(Currency, address)
            //    .ConfigureAwait(false);

            //if (walletAddress == null)
            //    return;

            //var txs = (await DataRepository
            //    .GetTransactionsAsync(Currency, fa2.TransactionType)
            //    .ConfigureAwait(false))
            //    .Cast<TezosTransaction>()
            //    .ToList();

            //var internalTxs = txs.Aggregate(new List<TezosTransaction>(), (list, tx) =>
            //{
            //    if (tx.InternalTxs != null)
            //        list.AddRange(tx.InternalTxs);

            //    return list;
            //});

            //var balance = 0m;
            //var unconfirmedIncome = 0m;
            //var unconfirmedOutcome = 0m;

            //foreach (var tx in txs.Concat(internalTxs))
            //{
            //    var isIncome = address == tx.To;
            //    var isOutcome = address == tx.From;
            //    var isConfirmed = tx.IsConfirmed;
            //    var isFailed = tx.State == BlockchainTransactionState.Failed;

            //    var income = isIncome && !isFailed
            //        ? tx.Amount.FromTokenDigits(fa2.DigitsMultiplier)
            //        : 0;

            //    var outcome = isOutcome && !isFailed
            //        ? -tx.Amount.FromTokenDigits(fa2.DigitsMultiplier)
            //        : 0;

            //    balance += isConfirmed ? income + outcome : 0;
            //    unconfirmedIncome += !isConfirmed ? income : 0;
            //    unconfirmedOutcome += !isConfirmed ? outcome : 0;
            //}

            //var fa2Api = fa2.BlockchainApi as ITokenBlockchainApi;

            //var balanceResult = await fa2Api
            //    .TryGetTokenBigMapBalanceAsync(
            //        address: address,
            //        pointer: fa2.TokenPointerBalance,
            //        cancellationToken: cancellationToken)
            //    .ConfigureAwait(false);

            //if (balanceResult.HasError)
            //{
            //    Log.Error("Error while balance update token for {@address} with code {@code} and description {@description}",
            //        address,
            //        balanceResult.Error.Code,
            //        balanceResult.Error.Description);
            //    return;
            //}

            //var balanceRes = balanceResult.Value.FromTokenDigits(fa2.DigitsMultiplier);

            //if (balance != balanceRes)
            //{
            //    Log.Warning("Transaction balance sum for address {@address} is {@balanceSum}, which is different from the actual address balance {@balance}",
            //        address,
            //        balance,
            //        balanceRes);

            //    balance = balanceRes;
            //}

            //var balanceDifference = balance - walletAddress.Balance;
            //var unconfirmedIncomeDifference = unconfirmedIncome - walletAddress.UnconfirmedIncome;
            //var unconfirmedOutcomeDifference = unconfirmedOutcome - walletAddress.UnconfirmedOutcome;

            //if (balanceDifference != 0 ||
            //    unconfirmedIncomeDifference != 0 ||
            //    unconfirmedOutcomeDifference != 0)
            //{
            //    walletAddress.Balance = balance;
            //    walletAddress.UnconfirmedIncome = unconfirmedIncome;
            //    walletAddress.UnconfirmedOutcome = unconfirmedOutcome;
            //    walletAddress.HasActivity = true;

            //    await DataRepository.UpsertAddressAsync(walletAddress)
            //        .ConfigureAwait(false);

            //    Balance += balanceDifference;
            //    UnconfirmedIncome += unconfirmedIncomeDifference;
            //    UnconfirmedOutcome += unconfirmedOutcomeDifference;

            //    RaiseBalanceUpdated(new CurrencyEventArgs(Currency));
            //}
        }

        #endregion Balances

        #region Addresses

        public override async Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            //var unspentAddresses = await DataRepository
            //    .GetUnspentAddressesAsync(Currency)
            //    .ConfigureAwait(false);

            //if (unspentAddresses.Any())
            //    return unspentAddresses.MaxBy(a => a.AvailableBalance());

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
        private JObject TransferParams(
            int tokenId,
            string from,
            string to,
            int amount)
        {
            return JObject.FromObject(new
            {
                entrypoint = "transfer",
                value = new object[]
                {
                    new
                    {
                        prim = "Pair",
                        args = new object[]
                        {
                            new
                            {
                                @string = from
                            },
                            new object[]
                            {
                                new
                                {
                                    prim = "Pair",
                                    args = new object[]
                                    {
                                        new
                                        {
                                            @string = to,
                                        },
                                        new
                                        {
                                            prim = "Pair",
                                            args = new object[]
                                            {
                                                new
                                                {
                                                    @int = tokenId.ToString()
                                                },
                                                new
                                                {
                                                    @int = amount.ToString()
                                                }
                                            }
                                        }
                                    }
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