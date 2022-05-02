using System;
using System.Collections.Generic;
using System.Linq;

using NBitcoin;
using NBitcoin.Policy;
using NBitcoinTransaction = NBitcoin.Transaction;
using Network = NBitcoin.Network;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin.Common;
using Atomex.Common;
using Atomex.Common.Memory;
using Transaction = Atomex.Blockchain.Abstract.Transaction;

namespace Atomex.Blockchain.Bitcoin
{
    public class BitcoinTransaction : Transaction
    {
        private readonly NBitcoinTransaction _tx;

        public override string TxId => _tx.GetHash().ToString();
        public override string Currency { get; set; }
        public override TransactionStatus Status { get; set; }
        public override DateTimeOffset? CreationTime { get; set; }
        public override DateTimeOffset? BlockTime { get; set; }
        public override long BlockHeight { get; set; }
        public override long Confirmations { get; set; }

        public IEnumerable<BitcoinTxInput> Inputs => _tx.Inputs
            .AsIndexedInputs()
            .Select(i => new BitcoinTxInput
            {
                Index = i.Index,
                PreviousOutput = new BitcoinTxPoint
                {
                    Hash  = i.PrevOut.Hash.ToString(),
                    Index = i.PrevOut.N
                },
                ScriptSig = i.ScriptSig.ToHex()
            });

        public IEnumerable<BitcoinTxOutput> Outputs(Network network) => _tx.Outputs
            .AsCoins()
            .Select(c => new BitcoinTxOutput
            {
                Currency         = Currency,
                Coin             = c,
                SpentTxPoints    = null,
                Address          = c.GetAddressOrDefault(network),
                IsConfirmed      = Confirmations > 0,
                IsSpentConfirmed = false
            });

        public BitcoinTransaction(
            string currency,
            NBitcoinTransaction tx,
            TransactionStatus status,
            DateTimeOffset creationTime,
            DateTimeOffset? blockTime,
            long blockHeight,
            long confirmations)
        {
            _tx = tx ?? throw new ArgumentNullException(nameof(tx));

            Currency      = currency ?? throw new ArgumentNullException(nameof(currency));
            Status        = status;
            CreationTime  = creationTime;
            BlockTime     = blockTime;
            BlockHeight   = blockHeight;
            Confirmations = confirmations;
        }

        public byte[] GetSignatureHash(
            BitcoinTxOutput output,
            Script redeemScript = null,
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

        public void SetSignature(Script sigScript, BitcoinTxOutput spentOutput)
        {
            var spentOutpoint = spentOutput.Coin.Outpoint;
            var input = _tx.Inputs.FindIndexedInput(spentOutpoint);

            if (spentOutput.IsSegWit) {
                input.WitScript = sigScript;
            } else {
                input.ScriptSig = sigScript;
            }
        }

        public void SetSignatures(
            IEnumerable<byte[]> signatures,
            IEnumerable<SecureBytes> publicKeys,
            IEnumerable<BitcoinInputToSign> outputsToSign)
        {
            var signaturesList = signatures.ToList();
            var publicKeysList = publicKeys.ToList();
            var outputsToSignList = outputsToSign.ToList();

            if (signaturesList.Count != outputsToSignList.Count)
                throw new ArgumentException(
                    $"The number of signatures ({signaturesList.Count}) and outputs ({outputsToSignList.Count}) does not match.");

            if (signaturesList.Count != publicKeysList.Count)
                throw new ArgumentException(
                    $"The number of signatures ({signaturesList.Count}) and public keys ({publicKeysList.Count}) does not match.");

            for (var i = 0; i < outputsToSignList.Count; ++i)
            {
                var signatureScript = outputsToSignList[i].CreateSignatureScript(
                    signaturesList[i],
                    publicKeysList[i]);

                SetSignature(signatureScript, outputsToSignList[i].Output);
            }
        }

        public void SetSequenceNumber(uint sequence, ICoin coin)
        {
            var input = _tx.Inputs.FirstOrDefault(t => t.PrevOut == coin.Outpoint);

            if (input != null)
                input.Sequence = new Sequence(sequence);
        }

        public bool Verify(
            IEnumerable<BitcoinTxOutput> outputs,
            Network network,
            out Error[] errors,
            bool checkScriptPubKey = true)
        {
            var coins = outputs
                .Select(o => o.Coin)
                .ToArray();

            var result = network
                .CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy
                {
                    CheckScriptPubKey = checkScriptPubKey,
                    ScriptVerify = ScriptVerify.Standard
                })
                .AddCoins(coins)
                .Verify(_tx, out var policyErrors);

            errors = policyErrors
                .Select(pe => new Error(Errors.VerificationError, pe.ToString()))
                .ToArray();

            return result;
        }

        public string ToHex() => _tx.ToHex();

        public static BitcoinTransaction Create(
            string currency,
            IEnumerable<ICoin> coins,
            IEnumerable<BitcoinDestination> recipients,
            Script change,
            decimal feeInSatoshi,
            DateTimeOffset lockTime,
            Network network,
            params Script[] knownRedeems)
        {
            var builder = network
                .CreateTransactionBuilder()
                .SetDustPrevention(false)
                .AddCoins(coins)
                .AddKnownRedeems(knownRedeems);

            foreach (var recipient in recipients)
                builder = builder.Send(recipient.Script, Money.Satoshis(recipient.AmountInSatoshi));

            var tx = builder.SendFees(Money.Satoshis(feeInSatoshi))
                .SetChange(change)
                .SetLockTime(lockTime != DateTimeOffset.MinValue
                    ? new LockTime(lockTime)
                    : LockTime.Zero)
                .BuildTransaction(false);

            return new BitcoinTransaction(
                currency: currency,
                tx: tx, 
                status: TransactionStatus.Pending,
                creationTime: DateTimeOffset.UtcNow,
                blockTime: null,
                blockHeight: 0,
                confirmations: 0);
        }
    }
}