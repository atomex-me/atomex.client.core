using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using NBitcoin;

using Atomex.Blockchain.Bitcoin;
using Atomex.Common;
using Atomex.Wallets.Abstract;
using Atomex.Wallets.Bitcoin.Abstract;

namespace Atomex.Wallets.Bitcoin
{
    public class BitcoinAccount : Account
    {
        private readonly BitcoinOutputsLocker _outputsLocker;

        public BitcoinConfig Configuration => CurrencyConfigProvider
            .GetByName<BitcoinConfig>(Currency);

        private readonly IOutputsDataRepository _outputsDataRepository;

        public BitcoinAccount(
            string currency,
            IWalletProvider walletProvider,
            ICurrencyConfigProvider currencyConfigProvider,
            IWalletDataRepository dataRepository,
            IOutputsDataRepository outputsRepository,
            ILogger logger = null)
            : base(
                currency,
                walletProvider,
                currencyConfigProvider,
                dataRepository,
                logger)
        {
            _outputsDataRepository = outputsRepository ?? throw new ArgumentNullException(nameof(outputsRepository));
            _outputsLocker = new BitcoinOutputsLocker();
        }

        #region Sending

        /// <summary>
        /// Creates, signs and broadcast transacton from <paramref name="inputsToSign"/> to <paramref name="destinations"/> with <paramref name="changeAddress"/> and custom fee in satoshi. If flag <paramref name="allowTxReplacement"/> is set it allows to use already spent unconfirmed inputs and tries to replace the old unconfirmed tx.
        /// </summary>
        /// <param name="inputsToSign">Tx inputs</param>
        /// <param name="destinations">Tx outputs</param>
        /// <param name="changeAddress">Change address, not used if all funds are transferred and change is zero</param>
        /// <param name="feeInSatoshi">Fee in satoshi</param>
        /// <param name="force">If flag is set, control of already locked outputs will be skipped
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Null if success, otherwise error</returns>
        public Task<(BitcoinTransaction tx, Error error)> SendAsync(
            IEnumerable<BitcoinInputToSign> inputsToSign,
            IEnumerable<BitcoinDestination> destinations,
            string changeAddress,
            decimal feeInSatoshi,
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run<(BitcoinTransaction tx, Error error)>(async () =>
            {
                try
                {
                    // inputs/outputs usage control
                    if (!force && !_outputsLocker.TryLock(inputsToSign.Select(i => i.Output)))
                        return (
                            tx: null,
                            error: new Error(
                                code: Errors.OutputsLockedError,
                                description: "Some of the outputs are already locked"));

                    var currencyConfig = Configuration;

                    var changeScript = BitcoinAddress
                        .Create(changeAddress, currencyConfig.Network)
                        .ScriptPubKey;

                    // transaction creation
                    var tx = BitcoinTransaction.Create(
                        currency: Currency,
                        coins: inputsToSign.Select(i => i.Output.Coin),
                        recipients: destinations,
                        change: changeScript,
                        feeInSatoshi: feeInSatoshi,
                        lockTime: DateTimeOffset.MinValue,
                        network: currencyConfig.Network);

                    // set sequence numbers
                    foreach (var input in inputsToSign)
                        if (input.Sequence > 0)
                            tx.SetSequenceNumber(input.Sequence, input.Output.Coin);

                    // transaction signing
                    var signError = await SignAsync(
                            tx: tx,
                            outputsToSign: inputsToSign,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (signError != null)
                        return (tx: null, error: signError);

                    // transaction verification
                    if (!tx.Verify(inputsToSign.Select(i => i.Output), currencyConfig.Network, out var errors))
                        return (
                            tx: null,
                            error: new Error(
                                code: Errors.VerificationError,
                                description: $"Transaction verification error: {string.Join(", ", errors.Select(e => e.Description))}"));

                    // transaction broadcast
                    var (txId, broadcastError) = await new BitcoinApi(
                            settings: currencyConfig.ApiSettings,
                            logger: Logger)
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
                    // unlock used outputs
                    if (!force)
                        _outputsLocker.Unlock(inputsToSign.Select(i => i.Output));
                }

            }, cancellationToken);
        }

        #endregion Sending

        #region Signing

        public Task<Error> SignAsync(
            BitcoinTransaction tx,
            IEnumerable<BitcoinInputToSign> outputsToSign,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var walletsOutputs = outputsToSign
                    .GroupBy(o => o.Output.WalletId);

                foreach (var walletOutputs in walletsOutputs)
                {
                    var error = await SignAsync(tx, walletOutputs.Key, walletOutputs, cancellationToken)
                        .ConfigureAwait(false);

                    if (error != null)
                        return error;
                }

                return null;

            }, cancellationToken);
        }

        private async Task<Error> SignAsync(
            BitcoinTransaction tx,
            int walletId,
            IEnumerable<BitcoinInputToSign> outputsToSign,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var walletInfo = await DataRepository
                    .GetWalletInfoByIdAsync(walletId, cancellationToken)
                    .ConfigureAwait(false);

                if (walletInfo == null)
                    return new Error(
                        code: Errors.SigningError,
                        description: $"Can't find wallet info with id {walletId} in account data repository");

                using var wallet = WalletProvider.GetWallet(walletInfo);

                if (wallet == null)
                    return new Error(
                        code: Errors.SigningError,
                        description: $"Can't create wallet with id {walletId}");

                // skip outputs without key path specified
                outputsToSign = outputsToSign
                    .Where(o => o.KeyPath != null || o.Output.KeyPath != null);

                var hashes = outputsToSign
                    .Select(o => new ReadOnlyMemory<byte>(tx.GetSignatureHash(o.Output, o.KnownRedeemScript, o.SigHash)))
                    .ToList();

                var keyPathes = outputsToSign
                    .Select(o => o.KeyPath ?? o.Output.KeyPath)
                    .ToList();

                var signatures = await wallet
                    .SignAsync(hashes, keyPathes, cancellationToken)
                    .ConfigureAwait(false);

                var publicKeys = await Task
                    .WhenAll(keyPathes
                        .Select(async kp => await wallet
                            .GetPublicKeyAsync(kp)
                            .ConfigureAwait(false)))
                    .ConfigureAwait(false);

                tx.SetSignatures(signatures, publicKeys, outputsToSign);

                // dispose public keys
                foreach (var publicKey in publicKeys)
                    publicKey.Dispose();

                return null; // no errors
            }
            catch (Exception e)
            {
                var error = $"Signing error: {e.Message}";

                Logger.LogError(error);

                return new Error(Errors.SigningError, error);
            }
        }

        #endregion Signing

        #region Balances

        public override IWalletScanner GetWalletScanner() =>
            new BitcoinWalletScanner(this, WalletProvider, Logger);

        #endregion Balances

        #region Outputs

        public Task<int> UpsertOutputsAsync(
            IEnumerable<BitcoinTxOutput> outputs,
            CancellationToken cancellationToken = default)
        {
            return _outputsDataRepository
                .UpsertOutputsAsync(outputs, cancellationToken);
        }

        #endregion Outputs
    }
}