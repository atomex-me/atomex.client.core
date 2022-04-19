using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Cryptography.Abstract;

namespace Atomex.Common
{
    public static class OrderExtensions
    {
        public static async Task CreateProofOfPossessionAsync(this Order order, IAccount account)
        {
            try
            {
                foreach (var address in order.FromWallets)
                {
                    if (address == null)
                        continue;

                    address.Nonce = Guid.NewGuid().ToString();

                    var data = Encoding.Unicode
                        .GetBytes($"{address.Nonce}{order.TimeStamp.ToUniversalTime():yyyy.MM.dd HH:mm:ss.fff}");

                    var hashToSign = HashAlgorithm.Sha256.Hash(data);

                    var currency = account.Currencies.GetByName(address.Currency);

                    var signature = await account.Wallet
                        .SignAsync(hashToSign, address, currency)
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

        public static bool IsContinuationOf(this Order order, Order previousOrder)
        {
            if (previousOrder == null)
                throw new ArgumentNullException(nameof(previousOrder));

            // check basic fields
            if (order.ClientOrderId != previousOrder.ClientOrderId ||
                order.Symbol != previousOrder.Symbol ||
                order.Price != previousOrder.Price ||
                order.Qty != previousOrder.Qty ||
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

            return true;
        }

        public static bool IsContinuationOf(this OrderStatus status, OrderStatus previousStatus)
        {
            return status switch
            {
                OrderStatus.Pending => false,
                OrderStatus.Placed => previousStatus == OrderStatus.Pending,
                OrderStatus.PartiallyFilled or OrderStatus.Filled or OrderStatus.Canceled =>
                    previousStatus == OrderStatus.Placed || previousStatus == OrderStatus.PartiallyFilled,
                OrderStatus.Rejected => previousStatus == OrderStatus.Pending,
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
            };
        }

        public static Order WithEndOfTransaction(this Order order, bool endOfTransaction)
        {
            order.EndOfTransaction = endOfTransaction;
            return order;
        }

        public static async Task<Order> ResolveWallets(
            this Order order,
            IAccount account,
            CancellationToken cancellationToken = default)
        {
            if (order.FromWallets == null)
                return order;

            foreach (var wallet in order.FromWallets)
            {
                if (wallet == null)
                    continue;

                var resolvedAddress = await account
                    .GetAddressAsync(wallet.Currency, wallet.Address, cancellationToken)
                    .ConfigureAwait(false);

                if (resolvedAddress == null)
                    throw new Exception($"Can't resolve wallet address {wallet.Address} for order {order.Id}");

                wallet.KeyIndex           = resolvedAddress.KeyIndex;
                wallet.Balance            = resolvedAddress.Balance;
                wallet.UnconfirmedIncome  = resolvedAddress.UnconfirmedIncome;
                wallet.UnconfirmedOutcome = resolvedAddress.UnconfirmedOutcome;
                wallet.PublicKey          = resolvedAddress.PublicKey;
            }

            return order;
        }

        public static string ToCompactString(this Order order)
        {
            return $"{{\"OrderId\": \"{order.Id}\", " +
                   $"\"ClientOrderId\": \"{order.ClientOrderId}\", " +
                   $"\"Symbol\": \"{order.Symbol}\", " +
                   $"\"Price\": \"{order.Price}\", " +
                   $"\"LastPrice\": \"{order.LastPrice}\", " +
                   $"\"Qty\": \"{order.Qty}\", " +
                   $"\"LeaveQty\": \"{order.LeaveQty}\", " +
                   $"\"LastQty\": \"{order.LastQty}\", " +
                   $"\"Side\": \"{order.Side}\", " +
                   $"\"Type\": \"{order.Type}\", " +
                   $"\"Status\": \"{order.Status}\", " +
                   $"\"EndOfTransaction\": \"{order.EndOfTransaction}\"}}";
        }
    }
}