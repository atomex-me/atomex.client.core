using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using NBitcoin;
using NBitcoin.Policy;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin.Common;
using Atomex.Common;

namespace Atomex.Blockchain.Bitcoin
{
    public class BitcoinTransaction : ITransaction
    {
        private readonly Transaction _tx;
        public string UniqueId => $"{Id}:{Currency}";
        public string Id => _tx.GetHash().ToString();
        public string Currency { get; }
        public TransactionStatus Status { get; set; }
        public DateTimeOffset? CreationTime { get; }
        public DateTimeOffset? BlockTime { get; }
        public long BlockHeight { get; }
        public long Confirmations { get; }
        public bool IsConfirmed => Confirmations > 0;
        public DateTime LockTime => _tx.LockTime.Date.UtcDateTime;
        public BigInteger ResolvedFee { get; }
        public BitcoinTxInput[] Inputs => _tx.Inputs
            .AsIndexedInputs()
            .Select(i => new BitcoinTxInput
            {
                Index = i.Index,
                PreviousOutput = new BitcoinTxPoint
                {
                    Index = i.PrevOut.N,
                    Hash = i.PrevOut.Hash.ToString()
                },
                ScriptSig = i.ScriptSig?.ToHex(),
                WitScript = i.WitScript?.ToBytes().ToHexString()
            })
            .ToArray();

        public BitcoinTxOutput[] Outputs => _tx.Outputs
            .AsCoins()
            .Select(c => new BitcoinTxOutput
            {
                Coin = c,
                Currency = Currency,
                SpentTxPoints = null,
                IsConfirmed = IsConfirmed,
                IsSpentConfirmed = false
            })
            .ToArray();

        public BitcoinTransaction()
        {
        }

        public BitcoinTransaction(
            string currency,
            Transaction tx,
            DateTimeOffset? creationTime = null,
            DateTimeOffset? blockTime = null,
            long blockHeight = 0,
            long confirmations = 0,
            BigInteger? fee = null)
        {
            _tx = tx;

            Currency  = currency;
            CreationTime = creationTime;
            BlockTime = blockTime;
            BlockHeight = blockHeight;
            Confirmations = confirmations;
            ResolvedFee = fee ?? BigInteger.Zero;

            Status = confirmations > 0
                ? TransactionStatus.Confirmed
                : TransactionStatus.Pending;
        }

        public void Sign(
            Key privateKey,
            BitcoinTxOutput spentOutput,
            Network network)
        {
            _tx.Sign(new BitcoinSecret(privateKey, network), spentOutput.Coin);
        }

        public void Sign(
            Key privateKey,
            BitcoinTxOutput[] spentOutputs,
            Network network)
        {
            foreach (var output in spentOutputs)
                Sign(privateKey, output, network);
        }

        public void SetSignature(Script sigScript, BitcoinTxOutput spentOutput)
        {
            var spentOutpoint = spentOutput.Coin.Outpoint;
            var input = _tx.Inputs.FindIndexedInput(spentOutpoint);

            if (spentOutput.IsSegWit)
            {
                input.WitScript = sigScript;
            }
            else
            {
                input.ScriptSig = sigScript;
            }
        }

        public void ClearSignatures(int inputNo)
        {
            if (_tx.Inputs[inputNo].ScriptSig != null)
                _tx.Inputs[inputNo].ScriptSig = Script.Empty;

            if (_tx.Inputs[inputNo].WitScript != null)
                _tx.Inputs[inputNo].WitScript = Script.Empty;
        }

        public bool Verify(
            BitcoinTxOutput spentOutput,
            Network network,
            bool checkScriptPubKey = true)
        {
            var result = network.CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy
                {
                    CheckScriptPubKey = checkScriptPubKey,
                    ScriptVerify = ScriptVerify.Standard
                })
                .AddCoins(spentOutput.Coin)
                .Verify(_tx, out _);

            return result;
        }

        public bool Verify(
            BitcoinTxOutput spentOutput,
            out Error[] errors,
            Network network,
            bool checkScriptPubKey = true)
        {
            var result = network
                .CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy
                {
                    CheckScriptPubKey = checkScriptPubKey,
                    ScriptVerify = ScriptVerify.Standard
                })
                .AddCoins(spentOutput.Coin)
                .Verify(_tx, out var policyErrors);

            errors = policyErrors
                .Select(pe => new Error(Errors.TransactionVerificationError, pe.ToString()))
                .ToArray();

            return result;
        }

        public bool Verify(
            IEnumerable<BitcoinTxOutput> spentOutputs,
            Network network,
            bool checkScriptPubKey = true)
        {
            var result = network
                .CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy
                {
                    CheckScriptPubKey = checkScriptPubKey,
                    ScriptVerify = ScriptVerify.Standard,
                })
                .AddCoins(spentOutputs
                    .Select(o => o.Coin)
                    .ToArray())
                .Verify(_tx, out var errors);

            return result;
        }

        public bool Verify(
            IEnumerable<BitcoinTxOutput> spentOutputs,
            out Error[] errors,
            Network network,
            bool checkScriptPubKey = true)
        {
            var result = network
                .CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy
                {
                    CheckScriptPubKey = checkScriptPubKey,
                    ScriptVerify = ScriptVerify.Standard
                })
                .AddCoins(spentOutputs
                    .Select(o => o.Coin)
                    .ToArray())
                .Verify(_tx, out var policyErrors);

            errors = policyErrors
                .Select(pe => new Error(Errors.TransactionVerificationError, pe.ToString()))
                .ToArray();

            return result;
        }

        public bool Check()
        {
            if (Outputs.Any(output => !output.IsValid))
                return false;

            return _tx.Check() == TransactionCheckResult.Success;
        }

        public long GetFee(BitcoinTxOutput[] spentOutputs) =>
            _tx.GetFee(spentOutputs
                .Select(o => o.Coin)
                .ToArray())
                .Satoshi;

        public byte[] GetSignatureHash(
            BitcoinTxOutput output,
            Script? redeemScript = null,
            SigHash sigHash = SigHash.All)
        {
            var coin = redeemScript == null
                ? output.Coin
                : new ScriptCoin(output.Coin, redeemScript);

            var input = _tx.Inputs
                .AsIndexedInputs()
                .FirstOrDefault(i => i.PrevOut.Hash == coin.Outpoint.Hash && i.PrevOut.N == coin.Outpoint.N);

            if (input == null)
                throw new Exception($"Transaction has no input for coin {coin.Outpoint.Hash}:{coin.Outpoint.N}");

            return _tx
                .GetSignatureHash(
                    scriptCode: coin.GetScriptCode(),
                    nIn: (int)input.Index,
                    nHashType: sigHash,
                    spentOutput: coin.TxOut,
                    sigversion: coin.GetHashVersion())
                .ToBytes();
        }

        public byte[] ToBytes() => _tx.ToBytes();
        public string ToHex() => _tx.ToHex();

        public void SetSequenceNumber(uint sequenceNumber)
        {
            foreach (var input in _tx.Inputs)
                input.Sequence = new Sequence(sequenceNumber);
        }

        public uint GetSequenceNumber(int inputIndex) =>
            _tx.Inputs[inputIndex].Sequence.Value;

        public static BitcoinTransaction CreateTransaction(
            string currency,
            IEnumerable<ICoin> coins,
            Script destination,
            Script change,
            long amount,
            long fee,
            Network network,
            DateTimeOffset? lockTime = null,
            Script[]? knownRedeems = null)
        {
            var txBuilder = network
                .CreateTransactionBuilder()
                .SetDustPrevention(false)
                .SetOptInRBF(true)
                .AddCoins(coins)
                .Send(destination, new Money(amount))
                .SendFees(new Money(fee))
                .SetChange(change);

            if (lockTime != null)
                txBuilder.SetLockTime(lockTime.Value);

            if (knownRedeems != null)
                txBuilder.AddKnownRedeems(knownRedeems);

            var tx = txBuilder
                .BuildTransaction(sign: false);

            return new BitcoinTransaction(
                currency: currency,
                tx: tx,
                creationTime: DateTimeOffset.UtcNow,
                fee: (long)tx.GetFee(coins.ToArray()).ToUnit(MoneyUnit.Satoshi));
        }
    }
}