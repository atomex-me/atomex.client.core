using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.Swaps;
using Atomix.Wallet.Abstract;
using NBitcoin;
using Serilog;

namespace Atomix.Common
{
    public static class OrderExtensions
    {
        public static Currency PurchasedCurrency(this Order order)
        {
            return order.Symbol.PurchasedCurrency(order.Side);
        }

        public static Currency SoldCurrency(this Order order)
        {
            return order.Symbol.SoldCurrency(order.Side);
        }

        public static bool IsSubsetOfWalletsSet(this Order order, IEnumerable<WalletAddress> wallets)
        {
            return order.FromWallets
                .Any(fw => wallets.FirstOrDefault(w => w.Address.Equals(fw.Address)) != null);
        }

        public static async Task CreateProofOfPossessionAsync(this Order order, IAccount account)
        {
            try
            {
                //var addresses = order.FromWallets.Concat(new[] {order.ToWallet, order.RefundWallet});
                var addresses = order.FromWallets.Concat(new[] {order.RefundWallet});

                foreach (var address in addresses)
                {
                    if (address == null)
                        continue;

                    address.Nonce = Guid.NewGuid().ToString();

                    var data = Encoding.Unicode
                        .GetBytes($"{address.Nonce}{order.TimeStamp:yyyy.MM.dd HH:mm:ss.fff}");

                    var signature = await account.Wallet
                        .SignAsync(data, address)
                        .ConfigureAwait(false);

                    if (signature == null)
                        throw new Exception("Error during creation of proof of possession. Sign is null.");

                    address.ProofOfPossession = Convert.ToBase64String(signature);

                    Log.Verbose("ProofOfPossession {@signature}", address.ProofOfPossession);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Proof of possession creating error");
            }
        }

        public static Error VerifyProofOfPossession(this Order order)
        {
            //var addresses = order.FromWallets.Concat(new[] { order.ToWallet, order.ChangeWallet });
            var addresses = order.FromWallets.Concat(new[] {order.RefundWallet });

            foreach (var address in addresses)
            {
                if (address == null)
                    continue;

                var data = Encoding.Unicode.GetBytes($"{address.Nonce}{order.TimeStamp:yyyy.MM.dd HH:mm:ss.fff}");
                var pubKeyBytes = address.PublicKeyBytes();

                if (!address.Currency.IsAddressFromKey(address.Address, pubKeyBytes))
                    return new Error(Errors.InvalidSigns, "Invalid public key for wallet");

                if (!address.Currency.VerifyMessage(pubKeyBytes, data, Convert.FromBase64String(address.ProofOfPossession)))
                   return new Error(Errors.InvalidSigns, "Invalid sign for wallet");
            }

            return null;
        }

        public static bool IsContinuationOf(this Order order, Order previousOrder)
        {
            if (previousOrder == null)
                throw new ArgumentNullException(nameof(previousOrder));

            // check basic fields
            if (order.ClientOrderId != previousOrder.ClientOrderId ||
                order.SymbolId != previousOrder.SymbolId ||
                order.Price != previousOrder.Price ||
                order.Qty != previousOrder.Qty ||
                order.Fee != previousOrder.Fee ||
                order.RedeemFee != previousOrder.RedeemFee ||
                order.Side != previousOrder.Side ||
                order.Type != previousOrder.Type)
            {
                return false;
            }

            // check statuses
            if (!order.Status.IsContinuationOf(previousOrder.Status))
                return false;

            // check leave qty
            switch (order.Status)
            {
                case OrderStatus.PartiallyFilled:
                case OrderStatus.Filled:
                    if (order.LeaveQty >= previousOrder.LeaveQty)
                        return false;
                    break;
                case OrderStatus.Canceled when order.LeaveQty > previousOrder.LeaveQty:
                    return false;
            }

            // check wallets
            if (order.FromWallets.Count != previousOrder.FromWallets.Count)
                return false;

            // todo: need Equals for WalletAddress
            if (order.FromWallets.Any(wa => previousOrder.FromWallets.FirstOrDefault(a => a.Address == wa.Address) == null))
                return false;

            if (order.RefundWallet.Address != previousOrder.RefundWallet.Address)
                return false;

            if (order.ToWallet.Address != previousOrder.ToWallet.Address)
                return false;

            return true;
        }

        public static bool IsContinuationOf(this OrderStatus status, OrderStatus previousStatus)
        {
            switch (status)
            {
                case OrderStatus.Unknown: 
                    return false;
                case OrderStatus.Pending:
                    return previousStatus == OrderStatus.Unknown;
                case OrderStatus.Placed:
                    return previousStatus == OrderStatus.Unknown ||
                           previousStatus == OrderStatus.Pending;
                case OrderStatus.Canceled:
                case OrderStatus.PartiallyFilled:
                case OrderStatus.Filled:
                    return previousStatus == OrderStatus.Placed ||
                           previousStatus == OrderStatus.PartiallyFilled;
                case OrderStatus.Rejected:
                    return previousStatus == OrderStatus.Unknown ||
                           previousStatus == OrderStatus.Placed;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
        }

        public static Order WithEndOfTransaction(this Order order, bool endOfTransaction)
        {
            order.EndOfTransaction = endOfTransaction;
            return order;
        }

        public static SwapRequisites ExtractRequisites(this Order order)
        {
            return new SwapRequisites(order.ToWallet, order.RefundWallet);
        }
    }
}