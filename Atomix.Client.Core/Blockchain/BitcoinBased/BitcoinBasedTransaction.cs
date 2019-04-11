using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Common;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;
using NBitcoin;
using NBitcoin.Policy;
using Serilog;

namespace Atomix.Blockchain.BitcoinBased
{
    public class BitcoinBasedTransaction : IBitcoinBasedTransaction
    {
        public const int DefaultConfirmations = 1;

        public Transaction Tx { get; }

        public string Id => Tx.GetHash().ToString();
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

        public Currency Currency { get; }
        public BlockInfo BlockInfo { get; }
        public long Fees => BlockInfo?.Fees ?? 0;
        public int Confirmations => BlockInfo?.Confirmations ?? 0;
        public long BlockHeight => BlockInfo?.BlockHeight ?? 0;
        public DateTime FirstSeen => BlockInfo?.FirstSeen ?? DateTime.MinValue;
        public DateTime BlockTime => BlockInfo?.BlockTime ?? DateTime.MinValue;

        public bool IsConfirmed() => Confirmations >= DefaultConfirmations;

        public BitcoinBasedTransaction(Currency currency, Transaction tx)
        {
            Currency = currency;
            Tx = tx;
            BlockInfo = new BlockInfo()
            {
                FirstSeen = DateTime.UtcNow
            };
        }

        public BitcoinBasedTransaction(BitcoinBasedCurrency currency, string hex)
            : this(currency, Transaction.Parse(hex, currency.Network))
        {
        }

        public BitcoinBasedTransaction(BitcoinBasedCurrency currency, byte[] bytes)
            : this(currency, bytes.ToHexString())
        {
        }

        public BitcoinBasedTransaction(
            Currency currency,
            Transaction tx,
            BlockInfo blockInfo)
            : this(currency, tx)
        {
            BlockInfo = blockInfo;
        }

        public long TotalOut => Tx.TotalOut.Satoshi;

        public async Task<bool> SignAsync(
            IPrivateKeyStorage keyStorage,
            IEnumerable<ITxOutput> spentOutputs,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (spentOutputs == null)
                throw new ArgumentNullException(nameof(spentOutputs));

            foreach (var spentOutput in spentOutputs)
            {
                var address = spentOutput.DestinationAddress(Currency);

                var keyIndex = await keyStorage
                    .RecoverKeyIndexAsync(Currency, address, cancellationToken)
                    .ConfigureAwait(false);

                if (keyIndex == null)
                {
                    Log.Error($"Can't find private key for address {address}");
                    return false;
                }

                Sign(keyStorage.GetPrivateKey(Currency, keyIndex), spentOutput);
            }

            return true;
        }

        public void Sign(Key privateKey, ITxOutput spentOutput)
        {
            var output = (BitcoinBasedTxOutput)spentOutput;

            Tx.Sign(privateKey, output.Coin);
        }

        public void Sign(byte[] privateKey, ITxOutput spentOutput)
        {
            Sign(new Key(privateKey), spentOutput);
        }

        public void Sign(Key privateKey, ITxOutput[] spentOutputs)
        {
            foreach (var output in spentOutputs)
                Sign(privateKey, output);
        }

        public void Sign(byte[] privateKey, ITxOutput[] spentOutputs)
        {
            Sign(new Key(privateKey), spentOutputs);
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

        public bool Verify(ITxOutput spentOutput)
        {
            if (!(Currency is BitcoinBasedCurrency btcBaseCurrency))
                throw new NotSupportedException("Currency must be Bitcoin based");

            var result = btcBaseCurrency.Network.CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy {CheckScriptPubKey = false, ScriptVerify = ScriptVerify.Standard})
                .AddCoins(((BitcoinBasedTxOutput)spentOutput).Coin)
                .Verify(Tx, out _);

            return result;
        }

        public bool Verify(ITxOutput spentOutput, out Error[] errors)
        {
            if (!(Currency is BitcoinBasedCurrency btcBaseCurrency))
                throw new NotSupportedException("Currency must be Bitcoin based");

            var result = btcBaseCurrency.Network.CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy {CheckScriptPubKey = false, ScriptVerify = ScriptVerify.Standard })
                .AddCoins(((BitcoinBasedTxOutput)spentOutput).Coin)
                .Verify(Tx, out var policyErrors);

            errors = policyErrors
                .Select(pe => new Error(Errors.TransactionVerificationError, pe.ToString()))
                .ToArray();

            return result;
        }

        public bool Verify(IEnumerable<ITxOutput> spentOutputs)
        {
            if (!(Currency is BitcoinBasedCurrency btcBaseCurrency))
                throw new NotSupportedException("Currency must be Bitcoin based");

            var result = btcBaseCurrency.Network.CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy {CheckScriptPubKey = false, ScriptVerify = ScriptVerify.Standard })
                .AddCoins(spentOutputs
                    .Cast<BitcoinBasedTxOutput>()
                    .Select(o => o.Coin)
                    .ToArray())
                .Verify(Tx, out _);

            return result;
        }

        public bool Verify(IEnumerable<ITxOutput> spentOutputs, out Error[] errors)
        {
            if (!(Currency is BitcoinBasedCurrency btcBaseCurrency))
                throw new NotSupportedException("Currency must be Bitcoin based");

            var result = btcBaseCurrency.Network.CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy {CheckScriptPubKey = false, ScriptVerify = ScriptVerify.Standard })
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
                blockInfo: (BlockInfo)BlockInfo?.Clone());
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
                .AddCoins(coins)
                .Send(destination, new Money(amount))
                .SendFees(new Money(fee))
                .SetChange(change)
                .SetLockTime(lockTime != DateTimeOffset.MinValue
                    ? new LockTime(lockTime)
                    : NBitcoin.LockTime.Zero)
                .BuildTransaction(false);

            return new BitcoinBasedTransaction(currency, tx);
        }
    }
}