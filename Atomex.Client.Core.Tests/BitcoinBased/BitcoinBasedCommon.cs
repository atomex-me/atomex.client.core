using System;
using System.Collections.Generic;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using NBitcoin;

namespace Atomex.Client.Core.Tests.BitcoinBased
{
    public static class BitcoinBasedCommon
    {
        public static IBitcoinBasedTransaction CreateFakeTx(BitcoinBasedCurrency currency, PubKey to, params long[] outputs)
        {
            var tx = Transaction.Create(currency.Network);

            foreach (var output in outputs)
                tx.Outputs.Add(new TxOut(new Money(output), to.Hash)); // p2pkh

            return new BitcoinBasedTransaction(currency, tx);
        }

        public static IBitcoinBasedTransaction CreateSegwitPaymentTx(
            BitcoinBasedCurrency currency,
            IEnumerable<ITxOutput> outputs,
            PubKey from,
            PubKey to,
            int amount,
            int fee)
        {
            return currency.CreateP2WPkhTx(
                unspentOutputs: outputs,
                destinationAddress: to.GetSegwitAddress(currency.Network).ToString(),
                changeAddress: from.GetSegwitAddress(currency.Network).ToString(),
                amount: amount,
                fee: fee);
        }

        public static IBitcoinBasedTransaction CreatePaymentTx(
            BitcoinBasedCurrency currency,
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