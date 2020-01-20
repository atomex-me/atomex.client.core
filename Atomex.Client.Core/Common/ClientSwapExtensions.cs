using System;
using System.Collections.Generic;
using System.Linq;
using Atomex.Abstract;
using Atomex.Core;

namespace Atomex.Common
{
    public static class ClientSwapExtensions
    {
        public static Swap ResolveSymbol(this Swap swap, ISymbols symbols)
        {
            swap.Symbol = symbols.FirstOrDefault(s => s.Name == swap.Symbol?.Name);

            if (swap.Symbol == null)
                throw new Exception("Symbol resolving error");

            return swap;
        }

        public static Swap ResolveRelationshipsByName(
            this Swap swap,
            IList<Symbol> symbols)
        {
            if (swap == null)
                return null;

            swap.Symbol = symbols.FirstOrDefault(s => s.Name == swap.Symbol?.Name);

            return swap;
        }

        public static Swap SetUserId(this Swap swap, string userId)
        {
            swap.UserId = userId;
            return swap;
        }

        public static void Cancel(this Swap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsCanceled;
        }

        public static void SetPaymentSigned(this Swap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsPaymentSigned;
        }

        public static void SetPaymentBroadcast(this Swap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;
        }

        public static void SetRefundSigned(this Swap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsRefundSigned;
        }

        public static void SetRefundBroadcast(this Swap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsRefundBroadcast;
        }

        public static void SetRedeemSigned(this Swap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsRedeemSigned;
        }

        public static void SetRedeemBroadcast(this Swap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsRedeemBroadcast;
        }
    }
}