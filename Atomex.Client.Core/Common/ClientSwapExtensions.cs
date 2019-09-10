using System;
using System.Collections.Generic;
using System.Linq;
using Atomex.Abstract;
using Atomex.Core.Entities;

namespace Atomex.Common
{
    public static class ClientSwapExtensions
    {
        public static ClientSwap ResolveSymbol(this ClientSwap swap, ISymbols symbols)
        {
            swap.Symbol = symbols.FirstOrDefault(s => s.Name == swap.Symbol?.Name);

            if (swap.Symbol == null)
                throw new Exception("Symbol resolving error");

            return swap;
        }

        public static ClientSwap ResolveRelationshipsByName(
            this ClientSwap swap,
            IList<Symbol> symbols)
        {
            if (swap == null)
                return null;

            swap.Symbol = symbols.FirstOrDefault(s => s.Name == swap.Symbol?.Name);

            return swap;
        }

        public static ClientSwap SetUserId(this ClientSwap swap, string userId)
        {
            swap.UserId = userId;
            return swap;
        }

        public static void Cancel(this ClientSwap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsCanceled;
        }

        public static void SetPaymentSigned(this ClientSwap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsPaymentSigned;
        }

        public static void SetPaymentBroadcast(this ClientSwap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;
        }

        public static void SetPaymentConfirmed(this ClientSwap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsPaymentConfirmed;
        }

        public static void SetRefundSigned(this ClientSwap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsRefundSigned;
        }

        public static void SetRefundBroadcast(this ClientSwap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsRefundBroadcast;
        }

        public static void SetRefundConfirmed(this ClientSwap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsRefundConfirmed;
        }

        public static void SetRedeemSigned(this ClientSwap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsRedeemSigned;
        }

        public static void SetRedeemBroadcast(this ClientSwap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsRedeemBroadcast;
        }

        public static void SetRedeemConfirmed(this ClientSwap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsRedeemConfirmed;
        }

        public static void SetHasPartyPayment(this ClientSwap swap)
        {
            swap.StateFlags |= SwapStateFlags.HasPartyPayment;
        }

        public static void SetPartyPaymentConfirmed(this ClientSwap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsPartyPaymentConfirmed;
        }
    }
}