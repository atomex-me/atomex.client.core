using System;
using System.Numerics;

namespace Atomex.Wallets
{
    public class Balance
    {
        /// <summary>
        /// Zero balance without update time
        /// </summary>
        public static Balance ZeroNoUpdates => new();
        /// <summary>
        /// Zero balance with current UTC time as last update time
        /// </summary>
        public static Balance Zero => new(
            total: 0,
            lastUpdateTime: DateTimeOffset.UtcNow);

        public BigInteger Total { get; set; }
        public BigInteger Received { get; set; }
        public BigInteger Sent { get; set; }
        public BigInteger UnconfirmedIncome { get; set; }
        public BigInteger UnconfirmedOutcome { get; set; }
        /// <summary>
        /// Last update time in UTC
        /// </summary>
        public DateTimeOffset LastUpdateTime { get; set; }

        public Balance(
            BigInteger total,
            BigInteger received,
            BigInteger sent,
            BigInteger unconfirmedIncome,
            BigInteger unconfirmedOutcome,
            DateTimeOffset lastUpdateTime)
        {
            Total              = total;
            Received           = received;
            Sent               = sent;
            UnconfirmedIncome  = unconfirmedIncome;
            UnconfirmedOutcome = unconfirmedOutcome;
            LastUpdateTime     = lastUpdateTime;
        }

        public Balance()
            : this(0, 0, 0, 0, 0, DateTimeOffset.MinValue)
        {
        }

        public Balance(
            BigInteger total,
            DateTimeOffset lastUpdateTime)
            : this(total, 0, 0, 0, 0, lastUpdateTime)
        {
        }

        public Balance Append(Balance balance)
        {
            Total              += balance.Total;
            Received           += balance.Received;
            Sent               += balance.Sent;
            UnconfirmedIncome  += balance.UnconfirmedIncome;
            UnconfirmedOutcome += balance.UnconfirmedOutcome;

            // take an earlier time as last update time
            if (LastUpdateTime > balance.LastUpdateTime || LastUpdateTime == DateTimeOffset.MinValue)
                LastUpdateTime = balance.LastUpdateTime;

            return this;
        }

        public bool IsZero() =>
            Total == 0 && UnconfirmedIncome == 0 && UnconfirmedOutcome == 0;

        public Balance Clone() => (Balance)MemberwiseClone();
    }
}