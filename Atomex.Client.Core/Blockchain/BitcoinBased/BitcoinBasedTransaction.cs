using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;
using NBitcoin.Policy;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Common.Memory;

namespace Atomex.Blockchain.BitcoinBased
{
    public class BitcoinBasedTransaction : IBlockchainTransaction
    {
        private const int DefaultConfirmations = 1;

        public Transaction Tx { get; }

        public string Id => Tx.GetHash().ToString();
        public string UniqueId => $"{Id}:{Currency}";

        public string Currency { get; }
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
        public BitcoinBasedTxOutput[] Outputs
        {
            get
            { 
                return Tx.Outputs
                    .AsCoins()
                    .Select(c => new BitcoinBasedTxOutput(c))
                    .ToArray();
            }
        }

        public BitcoinBasedTransaction(BitcoinBasedConfig currency, string hex)
            : this(currency.Name, Transaction.Parse(hex, currency.Network))
        {
        }

        public BitcoinBasedTransaction(BitcoinBasedConfig currency, byte[] bytes)
            : this(currency.Name, Transaction.Parse(bytes.ToHexString(), currency.Network))
        {
        }

        public BitcoinBasedTransaction(
            string currency,
            Transaction tx,
            BlockInfo blockInfo = null,
            long? fees = null)
        {
            Currency  = currency;
            Tx        = tx;
            BlockInfo = blockInfo;
            Fees      = fees;

            CreationTime = blockInfo != null
                ? blockInfo.FirstSeen ?? blockInfo.BlockTime
                : null;

            State = blockInfo != null
                ? blockInfo.Confirmations >= DefaultConfirmations
                    ? BlockchainTransactionState.Confirmed
                    : BlockchainTransactionState.Unconfirmed
                : BlockchainTransactionState.Pending;
        }

        public async Task<bool> SignAsync(
            IAddressResolver addressResolver,
            IKeyStorage keyStorage,
            IEnumerable<BitcoinBasedTxOutput> spentOutputs,
            CurrencyConfig currencyConfig,
            CancellationToken cancellationToken = default)
        {
            if (spentOutputs == null)
                throw new ArgumentNullException(nameof(spentOutputs));

            var btcBasedConfig = currencyConfig as BitcoinBasedConfig;

            foreach (var spentOutput in spentOutputs)
            {
                var address = spentOutput.DestinationAddress(btcBasedConfig.Network);

                var walletAddress = await addressResolver
                    .GetAddressAsync(
                        currency: Currency,
                        address: address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (walletAddress?.KeyIndex == null)
                {
                    Log.Error($"Can't find private key for address {address}");
                    return false;
                }

                using var securePrivateKey = keyStorage.GetPrivateKey(
                    currency: btcBasedConfig,
                    keyIndex: walletAddress.KeyIndex,
                    keyType: walletAddress.KeyType);

                Sign(securePrivateKey, spentOutput, btcBasedConfig);
            }

            return true;
        }

        public void Sign(
            Key privateKey,
            BitcoinBasedTxOutput spentOutput,
            BitcoinBasedConfig bitcoinBasedConfig)
        {
            Tx.Sign(new BitcoinSecret(privateKey, bitcoinBasedConfig.Network), spentOutput.Coin);
        }

        public void Sign(
            SecureBytes privateKey,
            BitcoinBasedTxOutput spentOutput,
            BitcoinBasedConfig bitcoinBasedConfig)
        {
            var scopedPrivateKey = privateKey.ToUnsecuredBytes();

            var key = new Key(scopedPrivateKey);

            Sign(key, spentOutput, bitcoinBasedConfig); // todo: do not use NBitcoin.Key
        }

        public void Sign(
            Key privateKey,
            BitcoinBasedTxOutput[] spentOutputs,
            BitcoinBasedConfig bitcoinBasedConfig)
        {
            foreach (var output in spentOutputs)
                Sign(privateKey, output, bitcoinBasedConfig);
        }

        public void Sign(
            SecureBytes privateKey,
            BitcoinBasedTxOutput[] spentOutputs,
            BitcoinBasedConfig bitcoinBasedConfig)
        {
            var scopedPrivateKey = privateKey.ToUnsecuredBytes();

            Sign(new Key(scopedPrivateKey), spentOutputs, bitcoinBasedConfig); // todo: do not use NBitcoin.Key
        }

        public void NonStandardSign(byte[] sigScript, BitcoinBasedTxOutput spentOutput)
        {
            NonStandardSign(new Script(sigScript), spentOutput);
        }

        public void NonStandardSign(Script sigScript, BitcoinBasedTxOutput spentOutput)
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

        public bool Verify(
            BitcoinBasedTxOutput spentOutput,
            BitcoinBasedConfig bitcoinBasedConfig,
            bool checkScriptPubKey = true)
        {
            var result = bitcoinBasedConfig.Network.CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy
                {
                    CheckScriptPubKey = checkScriptPubKey,
                    ScriptVerify = ScriptVerify.Standard
                })
                .AddCoins(spentOutput.Coin)
                .Verify(Tx, out _);

            return result;
        }

        public bool Verify(
            BitcoinBasedTxOutput spentOutput,
            out Error[] errors,
            BitcoinBasedConfig bitcoinBasedConfig,
            bool checkScriptPubKey = true)
        {
            var result = bitcoinBasedConfig.Network.CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy
                {
                    CheckScriptPubKey = checkScriptPubKey,
                    ScriptVerify = ScriptVerify.Standard
                })
                .AddCoins(spentOutput.Coin)
                .Verify(Tx, out var policyErrors);

            errors = policyErrors
                .Select(pe => new Error(Errors.TransactionVerificationError, pe.ToString()))
                .ToArray();

            return result;
        }

        public bool Verify(
            IEnumerable<BitcoinBasedTxOutput> spentOutputs,
            BitcoinBasedConfig bitcoinBasedConfig,
            bool checkScriptPubKey = true)
        {
            var result = bitcoinBasedConfig.Network.CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy
                {
                    CheckScriptPubKey = checkScriptPubKey,
                    ScriptVerify = ScriptVerify.Standard,
                })
                .AddCoins(spentOutputs
                    .Select(o => o.Coin)
                    .ToArray())
                .Verify(Tx, out var errors);

            return result;
        }

        public bool Verify(
            IEnumerable<BitcoinBasedTxOutput> spentOutputs,
            out Error[] errors,
            BitcoinBasedConfig bitcoinBasedConfig,
            bool checkScriptPubKey = true)
        {
            var result = bitcoinBasedConfig.Network.CreateTransactionBuilder()
                .SetTransactionPolicy(new StandardTransactionPolicy
                {
                    CheckScriptPubKey = checkScriptPubKey,
                    ScriptVerify = ScriptVerify.Standard
                })
                .AddCoins(spentOutputs
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

        public long GetFee(BitcoinBasedTxOutput[] spentOutputs)
        {
            return Tx.GetFee(spentOutputs
                    .Select(o => o.Coin)
                    .ToArray())
                .Satoshi;
        }

        public byte[] GetSignatureHash(
            BitcoinBasedTxOutput output,
            Script redeemScript = null,
            SigHash sigHash = SigHash.All)
        {
            var coin = redeemScript == null
                ? output.Coin
                : new ScriptCoin(output.Coin, redeemScript);

            var input = Tx.Inputs
                .AsIndexedInputs()
                .FirstOrDefault(i => i.PrevOut.Hash == coin.Outpoint.Hash && i.PrevOut.N == coin.Outpoint.N);

            if (input == null)
                throw new Exception($"Transaction has no input for coin {coin.Outpoint.Hash}:{coin.Outpoint.N}");

            return Tx
                .GetSignatureHash(
                    scriptCode: coin.GetScriptCode(),
                    nIn: (int)input.Index,
                    nHashType: sigHash,
                    spentOutput: coin.TxOut,
                    sigversion: coin.GetHashVersion())
                .ToBytes();
        }

        public Script GetScriptSig(int inputNo) =>
            Tx.Inputs[inputNo].ScriptSig;

        public byte[] ToBytes() =>
            Tx.ToBytes();

        public int VirtualSize() =>
            Tx.GetVirtualSize();

        public BitcoinBasedTransaction Clone()
        {
            return new BitcoinBasedTransaction(
                currency: Currency,
                tx: Tx.Clone(),
                blockInfo: (BlockInfo)BlockInfo?.Clone(),
                fees: Fees);
        }

        public long GetDust(long minOutputValue)
        {
            return Outputs
                .Where(output => output.Value < minOutputValue)
                .Sum(output => output.Value);
        }

        public void SetSequenceNumber(uint sequenceNumber)
        {
            foreach (var input in Tx.Inputs)
                input.Sequence = new Sequence(sequenceNumber);
        }

        public uint GetSequenceNumber(int inputIndex) =>
            Tx.Inputs[inputIndex].Sequence.Value;

        public static BitcoinBasedTransaction CreateTransaction(
            BitcoinBasedConfig currency,
            IEnumerable<ICoin> coins,
            Script destination,
            Script change,
            long amount,
            long fee,
            params Script[] knownRedeems)
        {
            return CreateTransaction(
                currency: currency,
                coins: coins,
                destination: destination,
                change: change,
                amount: amount,
                fee: fee,
                lockTime: DateTimeOffset.MinValue,
                knownRedeems: knownRedeems);
        }

        public static BitcoinBasedTransaction CreateTransaction(
            BitcoinBasedConfig currency,
            IEnumerable<ICoin> coins,
            Script destination,
            Script change,
            long amount,
            long fee,
            DateTimeOffset lockTime,
            params Script[] knownRedeems)
        {
            var tx = currency.Network.CreateTransactionBuilder()
                .SetDustPrevention(false)
                .SetOptInRBF(true)
                .AddCoins(coins)
                .Send(destination, new Money(amount))
                .SendFees(new Money(fee))
                .SetChange(change)
                .SetLockTime(lockTime != DateTimeOffset.MinValue
                    ? new LockTime(lockTime)
                    : NBitcoin.LockTime.Zero)
                .AddKnownRedeems(knownRedeems)
                .BuildTransaction(false);

            return new BitcoinBasedTransaction(
                currency: currency.Name,
                tx: tx,
                blockInfo: null,
                fees: (long)tx.GetFee(coins.ToArray()).ToUnit(MoneyUnit.Satoshi));
        }
    }
}