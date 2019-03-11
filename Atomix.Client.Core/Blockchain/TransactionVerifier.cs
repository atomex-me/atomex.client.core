using System;
using Atomix.Blockchain.Abstract;
using Atomix.Core;
using Atomix.Core.Entities;

namespace Atomix.Blockchain
{
    public class TransactionVerifier
    {
        public static bool TryVerifyPaymentTx(
            IBlockchainTransaction tx,
            Order order,
            out Error error)
        {
            if (tx == null)
                throw new ArgumentNullException(nameof(tx));

            if (order == null)
                throw new ArgumentNullException(nameof(order));

            if (tx.Currency is BitcoinBasedCurrency)
                return TryVerifyBitcoinBasePaymentTx(tx, order, out error);

            throw new NotSupportedException($"Currency {tx.Currency.Name} not supported!");
        }

        public static bool TryVerifyRefundTx(
            IBlockchainTransaction refundTx,
            Order order,
            out Error error)
        {
            error = null;
            return true;
            //throw new NotImplementedException();
        }

        public static bool TryVerifySignedRefundTx(
            IBlockchainTransaction signedRefundTx,
            Order order,
            out Error error)
        {
            error = null;
            return true;
            //throw new NotImplementedException();
        }

        public static bool TryVerifySignedRefundTx(
            IBlockchainTransaction refundTx,
            IBlockchainTransaction paymentTx,
            Order order,
            out Error error)
        {
            throw new NotImplementedException();
        }

        public static bool TryVerifyBitcoinBasePaymentTx(
            IBlockchainTransaction tx,
            Order order,
            out Error error)
        {
            error = null;
            return true;
            //throw new NotImplementedException();
            //    var currency = (BitcoinBaseCurrency) tx.Currency;

            //    if (order.SwapInitiative) // is initiator
            //    {
            //        // 1. check inputs
            //        foreach (var input in tx.Inputs.Cast<BitcoinBaseTxPoint>())
            //        {
            //            if (!input.IsStandard) {
            //                error = new Error(Errors.NotSupported, $"Non standard input {{{input.Hash}:{input.Index}}}");
            //                return false;
            //            }


            //            //order.FromWallets

            //        }

            //        foreach (var output in tx.Outputs.Cast<BitcoinBaseTxOutput>())
            //        {
            //            // 2. find and check target script
            //            // 3. check change output
            //        }

            //        // 4. check total fee
            //    }
            //    else // is contragent
            //    {

            //    }

            //    throw new NotImplementedException();
        }
    }
}