using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Ethereum;
using Atomex.Common;
using Atomex.Wallets.Abstract;
using Atomex.Wallets.Common;
using Atomex.Wallets.Ethereum.Common;

namespace Atomex.Wallets.Ethereum
{
    public class EthereumAccount : Account
    {
        private static ResourceLocker<string> _addressLocker;
        public static ResourceLocker<string> AddressLocker
        {
            get
            {
                var instance = _addressLocker;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _addressLocker, new ResourceLocker<string>(), null);
                    instance = _addressLocker;
                }

                return instance;
            }
        }

        public EthereumConfig Configuration => CurrencyConfigProvider
            .GetByName<EthereumConfig>(Currency);

        public EthereumAccount(
            string currency,
            IWalletProvider walletProvider,
            ICurrencyConfigProvider currencyConfigProvider,
            IWalletDataRepository dataRepository,
            ILogger logger = null)
            : base(
                currency,
                walletProvider,
                currencyConfigProvider,
                dataRepository,
                logger)
        {
        }

        #region Sending

        public Task<(EthereumTransaction tx, Error error)> SendAsync(
            string from,
            string to,
            decimal amount,
            GasPrice gasPrice,
            GasLimit gasLimit,
            Nonce nonce,
            string data = null,
            CancellationToken cancellationToken = default)
        {
            if (gasPrice == null)
                throw new ArgumentNullException(nameof(gasPrice));

            if (gasLimit == null)
                throw new ArgumentNullException(nameof(gasLimit));

            if (nonce == null)
                throw new ArgumentNullException(nameof(nonce));

            return Task.Run(async () =>
            {
                try
                {
                    var walletAddress = await GetAddressAsync(from, cancellationToken)
                        .ConfigureAwait(false);

                    var currencyConfig = Configuration;

                    var api = new EthereumApi(
                        settings: currencyConfig.ApiSettings,
                        logger: Logger);

                    // gas price
                    var (gasPriceValue, gasPriceError) = await gasPrice
                        .ResolveGasPriceAsync(
                            api,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (gasPriceError != null)
                        return (tx: null, error: gasPriceError);

                    if (gasPriceValue == null)
                        return (tx: null, error: new Error(Errors.NullGasPriceError, "Gas price is null"));

                    // gas limit
                    var (gasLimitValue, gasLimitError) = await gasLimit
                        .GetGasLimitAsync(
                            api: api,
                            to: to,
                            from: from,
                            value: EthereumHelper.EthToWei(amount),
                            gasPrice: EthereumHelper.GweiToWei(gasPriceValue.Value),
                            data: data,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (gasLimitError != null)
                        return (tx: null, error: gasLimitError);

                    if (gasLimitValue == null)
                        return (tx: null, error: new Error(Errors.NullGasLimitError, "Gas limit is null"));

                    // transaction creation
                    var tx = new EthereumTransaction
                    {
                        Currency     = Currency,
                        CreationTime = DateTimeOffset.UtcNow,
                        From         = from,
                        To           = to,
                        Amount       = EthereumHelper.EthToWei(amount),
                        GasLimit     = new BigInteger(gasLimitValue.Value),
                        GasPrice     = EthereumHelper.GweiToWei(gasPriceValue.Value),
                        Data         = data,
                        ChainId      = (int)currencyConfig.Chain
                    };

                    var (sendTx, sendError) = await SendAsync(
                            tx: tx,
                            sign: true,
                            nonce: nonce,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    return (tx: sendTx, error: sendError);
                }
                catch (Exception e)
                {
                    var error = $"Sending error: {e.Message}";

                    Logger.LogError(e, error);

                    return (tx: null, error: new Error(Errors.SendingError, error));
                }

            }, cancellationToken);
        }

        public Task<(EthereumTransaction tx, Error error)> SendAsync(
            EthereumTransaction tx,
            bool sign = true,
            Nonce nonce = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run<(EthereumTransaction tx, Error error)>(async () =>
            {
                try
                {
                    var currencyConfig = Configuration;

                    var api = new EthereumApi(
                        settings: currencyConfig.ApiSettings,
                        logger: Logger);

                    // resolve nonce
                    if (nonce != null)
                    {
                        // lock address to prevent races for nonces
                        if (nonce.UseSync)
                            await AddressLocker
                                .LockAsync(tx.From, cancellationToken)
                                .ConfigureAwait(false);

                        var (nonceValue, nonceError) = await nonce
                            .GetNonceAsync(
                                api: api,
                                from: tx.From,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (nonceError != null)
                            return (tx: null, error: nonceError);

                        if (nonceValue == null)
                            return (
                                tx: null,
                                error: new Error(Errors.NullNonceError, "Nonce is null"));

                        tx.Nonce = nonceValue.Value;
                    }

                    // transaction signing
                    if (sign)
                    {
                        var signError = await SignAsync(tx, cancellationToken)
                            .ConfigureAwait(false);

                        if (signError != null)
                            return (tx: null, error: signError);
                    }
                    else if (tx.Signature == null)
                    {
                        return (
                            tx: null,
                            error: new Error(
                                code: Errors.SendingError,
                                description: "Transaction is not signed"));
                    }

                    // transaction verification
                    if (!tx.Verify())
                        return (
                            tx: null,
                            error: new Error(
                                code: Errors.VerificationError,
                                description: $"Transaction verification error"));

                    // transaction broadcast
                    var (txId, broadcastError) = await api
                        .BroadcastAsync(tx, cancellationToken)
                        .ConfigureAwait(false);

                    if (broadcastError != null)
                        return (tx: null, error: broadcastError);

                    var upsertResult = DataRepository
                        .UpsertTransactionAsync(tx, cancellationToken)
                        .ConfigureAwait(false);

                    return (tx, error: null);
                }
                catch (Exception e)
                {
                    var error = $"Sending error: {e.Message}";

                    Logger.LogError(e, error);

                    return (tx: null, error: new Error(Errors.SendingError, error));
                }
                finally
                {
                    // unlock address
                    if (nonce != null && nonce.UseSync)
                        AddressLocker.Unlock(tx.From);
                }

            }, cancellationToken);
        }

        #endregion Sending

        #region Signing

        public Task<Error> SignAsync(
            EthereumTransaction tx,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var walletAddress = await GetAddressAsync(tx.From, cancellationToken)
                        .ConfigureAwait(false);

                    var walletInfo = await DataRepository
                        .GetWalletInfoByIdAsync(walletAddress.WalletId, cancellationToken)
                        .ConfigureAwait(false);

                    if (walletInfo == null)
                        return new Error(
                            code: Errors.SigningError,
                            description: $"Can't find wallet info with id {walletAddress.WalletId} in account data repository");

                    using var wallet = WalletProvider.GetWallet(walletInfo);

                    if (wallet == null)
                        return new Error(
                            code: Errors.SigningError,
                            description: $"Can't create wallet with id {walletAddress.WalletId}");

                    tx.Signature = await wallet
                        .SignAsync(
                            data: tx.GetRawHash(),
                            keyPath: walletAddress.KeyPath,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    return null; // no errors
                }
                catch (Exception e)
                {
                    var error = $"Signing error: {e.Message}";

                    Logger.LogError(error);

                    return new Error(Errors.SigningError, error);
                }

            }, cancellationToken);
        }

        #endregion Signing

        #region Balances

        public override IWalletScanner GetWalletScanner() =>
            new EthereumWalletScanner(this, WalletProvider, Logger);

        #endregion Balances

        #region Common

        public static Task<ResourceLock<string>> LockAddressAsync(
            string address,
            CancellationToken cancellationToken = default) =>
            AddressLocker.GetLockAsync(address, cancellationToken);

        #endregion Common
    }
}