using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using NBitcoin;
using NBitcoin.Policy;
using Serilog;

namespace Atomex.Blockchain.BitcoinBased
{
    public class BitcoinBasedTransaction : IBitcoinBasedTransaction
    {
        private const int DefaultConfirmations = 1;

        private Transaction Tx { get; }

        public string Id => Tx.GetHash().ToString();
        public string UniqueId => $"{Id}:{Currency.Name}";

        public Currency Currency { get; }
        public BlockInfo BlockInfo { get; }
        public BlockchainTransactionState State { get; set; }
        public BlockchainTransactionType Type { get; set; }
        public DateTime? CreationTime { get; set; }
        public long? Fees { get; set; }

        public bool IsConfirmed => (BlockInfo?.Confirmations ?? 0) >= DefaultConfirmations;
        public long TotalOut => Tx.TotalOut.Satoshi;
        public long Amount { get; set; }
        public DateTime LockTime => Tx.LockTime.Date.UtcDateTime;

        public ITxPoint[] Inputs
        {
            get
            {
                return Tx.Inputs
                    .AsIndexedInputs()
                    .Select(i => new BitcoinBasedTxPoint(i))
                    .Cast<ITxPoint>()
                    .ToArray();
            }
        }
        public ITxOutput[] Outputs
        {
            get
            { 
                return Tx.Outputs
                    .AsCoins()
                    .Select(c => new BitcoinBasedTxOutput(c))
                    .Cast<ITxOutput>()
                    .ToArray();
            }
        }

        public BitcoinBasedTransaction(BitcoinBasedCurrency currency, string hex)
            : this(currency, Transaction.Parse(hex, currency.Network))
        {
        }

        public BitcoinBasedTransaction(BitcoinBasedCurrency currency, byte[] bytes)
            : this(currency, Transaction.Parse(bytes.ToHexString(), currency.Network))
        {
        }

        public BitcoinBasedTransaction(
            Currency currency,
            Transaction tx,
            BlockInfo blockInfo = null,
            long? fees = null)
        {
            Currency = currency;
            Tx = tx;
            BlockInfo = blockInfo;
            Fees = fees;

            CreationTime = blockInfo != null
                ? blockInfo.FirstSeen ?? (blockInfo.BlockTime ?? DateTime.UtcNow)
                : DateTime.UtcNow;

            State = blockInfo != null
                ? blockInfo.Confirmations >= DefaultConfirmations
                    ? BlockchainTransactionState.Confirmed
                    : BlockchainTransactionState.Unconfirmed
                : BlockchainTransactionState.Pending;
        }

        public async Task<bool> SignAsync(
            IAddressResolver addressResolver,
            IKeyStorage keyStorage,
            IEnumerable<ITxOutput> spentOutputs,
            CancellationToken cancellationToken = default)
        {
            if (spentOutputs == null)
                throw new ArgumentNullException(nameof(spentOutputs));

            foreach (var spentOutput in spentOutputs)
            {
                var address = spentOutput.DestinationAddress(Currency);

                var walletAddress = await addressResolver
                    .GetAddressAsync(
                        currency: Currency.Name,
                        address: address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (walletAddress?.KeyIndex == null)
                {
                    Log.Error($"Can't find private key for address {address}");
                    return false;
                }

                using var securePrivateKey = keyStorage.GetPrivateKey(Currency, walletAddress.KeyIndex);

                Sign(securePrivateKey, spentOutput);
            }

            return true;
        }

        public void Sign(Key privateKey, ITxOutput spentOutput)
        {
            var output = (BitcoinBasedTxOutput)spentOutput;
            var currency = (BitcoinBasedCurrency)Currency;

            Tx.Sign(new BitcoinSecret(privateKey, currency.Network), output.Coin);
        }

        public void Sign(SecureBytes privateKey, ITxOutput spentOutput)
        {
            using var scopedPrivateKey = privateKey.ToUnsecuredBytes();

            Sign(new Key(scopedPrivateKey), spentOutput); // todo: do not use NBitcoin.Key
        }

        public void Sign(Key privateKey, ITxOutput[] spentOutputs)
        {
            foreach (var output in spentOutputs)
                Sign(privateKey, output);
        }

        public void Sign(SecureBytes privateKey, ITxOutput[] spentOutputs)
        {
            using var scopedPrivateKey = privateKey.ToUnsecuredBytes();

            Sign(new Key(scopedPrivateKey), spentOutputs); // todo: do not use NBitcoin.Key
        }

        public void NonStandardSign(byte[] sigScript, ITxOutput spentOutput)
        {
            NonStandardSign(new Script(sigScript), spentOutput);
        }

        public void NonStandardSign(Script sigScript, ITxOutput spentOutput)
        {
            var spentOutpoint = ((BitcoinBasedTxOutput) spentOutput).Coin.Outpoint;
            var input = Tx.Inputs.FindIndexedInput(spentOutpoint);

            input.ScriptSig = sigScript;
        }

        public void NonStandardSign(byte[] sigScript, int inputNo)
        {
            NonStandardSign(new Script(sigScript), inputNo);
        }

        public void NonStandardSign(Script sigScript, int inputNo)
        {
            var input = Tx.Inputs[inputNo];

            input.ScriptSig = sigScript;
        }

        public bool Verify(ITxOutput spentOutput, bool checkScriptPubKey = true)
        {
            if (!(Currency is BitcoinBasedCurrency btcBaseCurrency))
                throw new NotSupportedException("Currency must be Bitcoin based");

            var result = btcBaseCurrency.Network.CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy
                {
                    CheckScriptPubKey = checkScriptPubKey,
                    ScriptVerify = ScriptVerify.Standard
                })
                .AddCoins(((BitcoinBasedTxOutput)spentOutput).Coin)
                .Verify(Tx, out _);

            return result;
        }

        public bool Verify(ITxOutput spentOutput, out Error[] errors, bool checkScriptPubKey = true)
        {
            if (!(Currency is BitcoinBasedCurrency btcBaseCurrency))
                throw new NotSupportedException("Currency must be Bitcoin based");

            var result = btcBaseCurrency.Network.CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy
                {
                    CheckScriptPubKey = checkScriptPubKey,
                    ScriptVerify = ScriptVerify.Standard
                })
                .AddCoins(((BitcoinBasedTxOutput)spentOutput).Coin)
                .Verify(Tx, out var policyErrors);

            errors = policyErrors
                .Select(pe => new Error(Errors.TransactionVerificationError, pe.ToString()))
                .ToArray();

            return result;
        }

        public bool Verify(IEnumerable<ITxOutput> spentOutputs, bool checkScriptPubKey = true)
        {
            if (!(Currency is BitcoinBasedCurrency btcBaseCurrency))
                throw new NotSupportedException("Currency must be Bitcoin based");

            var result = btcBaseCurrency.Network.CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy
                {
                    CheckScriptPubKey = checkScriptPubKey,
                    ScriptVerify = ScriptVerify.Standard
                })
                .AddCoins(spentOutputs
                    .Cast<BitcoinBasedTxOutput>()
                    .Select(o => o.Coin)
                    .ToArray())
                .Verify(Tx, out _);

            return result;
        }

        public bool Verify(IEnumerable<ITxOutput> spentOutputs, out Error[] errors, bool checkScriptPubKey = true)
        {
            if (!(Currency is BitcoinBasedCurrency btcBaseCurrency))
                throw new NotSupportedException("Currency must be Bitcoin based");

            var result = btcBaseCurrency.Network.CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy
                {
                    CheckScriptPubKey = checkScriptPubKey,
                    ScriptVerify = ScriptVerify.Standard
                })
                .AddCoins(spentOutputs
                    .Cast<BitcoinBasedTxOutput>()
                    .Select(o => o.Coin)
                    .ToArray())
                .Verify(Tx, out var policyErrors);

            errors = policyErrors
                .Select(pe => new Error(Errors.TransactionVerificationError, pe.ToString()))
                .ToArray();

            return result;
        }

        public bool Check()
        {
            if (Outputs.Any(output => !output.IsValid))
                return false;

            return Tx.Check() == TransactionCheckResult.Success;
        }

        public long GetFee(ITxOutput[] spentOutputs)
        {
            return Tx.GetFee(spentOutputs
                    .Cast<BitcoinBasedTxOutput>()
                    .Select(o => o.Coin)
                    .ToArray())
                .Satoshi;
        }

        public byte[] GetSignatureHash(ITxOutput spentOutput)
        {
            return Tx.GetSignatureHash(((BitcoinBasedTxOutput)spentOutput).Coin).ToBytes();
        }

        public byte[] GetSignatureHash(Script redeemScript, ITxOutput spentOutput)
        {
            var coin = ((BitcoinBasedTxOutput) spentOutput).Coin;

            var scriptCoint = new ScriptCoin(coin, redeemScript);

            return Tx.GetSignatureHash(scriptCoint).ToBytes();
        }

        public Script GetScriptSig(int inputNo)
        {
            return Tx.Inputs[inputNo].ScriptSig;
        }

        public byte[] ToBytes()
        {
            return Tx.ToBytes();
        }

        public int VirtualSize()
        {
            return Tx.GetVirtualSize();
        }

        public IBitcoinBasedTransaction Clone()
        {
            return new BitcoinBasedTransaction(
                currency: (BitcoinBasedCurrency)Currency,
                tx: Tx.Clone(),
                blockInfo: (BlockInfo)BlockInfo?.Clone(),
                fees: Fees);
        }

        public long GetDust()
        {
            var currency = (BitcoinBasedCurrency)Currency;

            return Outputs
                .Where(output => output.Value < currency.GetDust())
                .Sum(output => output.Value);
        }

        public static BitcoinBasedTransaction CreateTransaction(
            BitcoinBasedCurrency currency,
            IEnumerable<ICoin> coins,
            Script destination,
            Script change,
            long amount,
            long fee)
        {
            return CreateTransaction(
                currency: currency,
                coins: coins,
                destination: destination,
                change: change,
                amount: amount,
                fee: fee,
                lockTime: DateTimeOffset.MinValue);
        }

        public static BitcoinBasedTransaction CreateTransaction(
            BitcoinBasedCurrency currency,
            IEnumerable<ICoin> coins,
            Script destination,
            Script change,
            long amount,
            long fee,
            DateTimeOffset lockTime)
        {
            var tx = currency.Network.CreateTransactionBuilder()
                .SetDustPrevention(false)
                .AddCoins(coins)
                .Send(destination, new Money(amount))
                .SendFees(new Money(fee))
                .SetChange(change)
                .SetLockTime(lockTime != DateTimeOffset.MinValue
                    ? new LockTime(lockTime)
                    : NBitcoin.LockTime.Zero)
                .BuildTransaction(false);

            return new BitcoinBasedTransaction(
                currency: currency,
                tx: tx,
                blockInfo: null,
                fees: (long)tx.GetFee(coins.ToArray()).ToUnit(MoneyUnit.Satoshi));
        }
    }
}