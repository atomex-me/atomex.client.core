using System;

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

        public decimal Total { get; set; }
        public decimal Received { get; set; }
        public decimal Sent { get; set; }
        public decimal UnconfirmedIncome { get; set; }
        public decimal UnconfirmedOutcome { get; set; }
        /// <summary>
        /// Last update time in UTC
        /// </summary>
        public DateTimeOffset LastUpdateTime { get; set; }

        public Balance(
            decimal total,
            decimal received,
            decimal sent,
            decimal unconfirmedIncome,
            decimal unconfirmedOutcome,
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
            decimal total,
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