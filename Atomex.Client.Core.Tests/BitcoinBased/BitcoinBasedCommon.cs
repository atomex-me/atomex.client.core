using System;
using System.Collections.Generic;

using NBitcoin;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;

namespace Atomex.Client.Core.Tests
{
    public static class BitcoinBasedCommon
    {
        public static IBitcoinBasedTransaction_OLD CreateFakeTx(BitcoinBasedConfig_OLD currency, PubKey to, params long[] outputs)
        {
            var tx = Transaction.Create(currency.Network);

            foreach (var output in outputs)
                tx.Outputs.Add(new TxOut(new Money(output), to.Hash)); // p2pkh

            return new BitcoinBasedTransaction(currency.Name, tx);
        }

        public static IBitcoinBasedTransaction_OLD CreateSegwitPaymentTx(
            BitcoinBasedConfig_OLD currency,
            IEnumerable<ITxOutput> outputs,
            PubKey from,
            PubKey to,
            int amount,
            int fee)
        {
            return currency.CreateP2WPkhTx(
                unspentOutputs: outputs,
                destinationAddress: to.GetAddress(ScriptPubKeyType.Segwit, currency.Network).ToString(),
                changeAddress: from.GetAddress(ScriptPubKeyType.Segwit, currency.Network).ToString(),
                amount: amount,
                fee: fee);
        }

        public static IBitcoinBasedTransaction_OLD CreatePaymentTx(
            BitcoinBasedConfig_OLD currency,
            IEnumerable<ITxOutput> outputs,
            PubKey from,
            PubKey to,
            int amount,
            int fee,
            DateTimeOffset lockTime,
            params Script[] knownRedeems)
        {
            return currency.CreateP2PkhTx(
                unspentOutputs: outputs,
                destinationAddress: to.GetAddress(ScriptPubKeyType.Legacy, currency.Network).ToString(),
                changeAddress: from.GetAddress(ScriptPubKeyType.Legacy, currency.Network).ToString(),
                amount: amount,
                fee: fee,
                lockTime: lockTime,
                knownRedeems: knownRedeems);
        }
    }
}