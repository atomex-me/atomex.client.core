using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;

namespace Atomex.TezosTokens
{
    public class TezosTokenConfig : TezosConfig
    {
        public long TransferFee { get; protected set; }
        public long TransferGasLimit { get; protected set; }
        public long TransferStorageLimit { get; protected set; }
        public long TransferSize { get; protected set; }

        public long ApproveFee { get; protected set; }
        public long ApproveGasLimit { get; protected set; }
        public long ApproveStorageLimit { get; protected set; }
        public long ApproveSize { get; protected set; }

        public string TokenContractAddress { get; protected set; }
        public int TokenId { get; protected set; }
        public string ViewContractAddress { get; protected set; }

        public override async Task<decimal> GetRewardForRedeemAsync(
            decimal maxRewardPercent,
            decimal maxRewardPercentInBase,
            string feeCurrencyToBaseSymbol,
            decimal feeCurrencyToBasePrice,
            string feeCurrencySymbol = null,
            decimal feeCurrencyPrice = 0,
            CancellationToken cancellationToken = default)
        {
            var rewardForRedeemInXtz = await base.GetRewardForRedeemAsync(
                maxRewardPercent: maxRewardPercent,
                maxRewardPercentInBase: maxRewardPercentInBase,
                feeCurrencyToBaseSymbol: feeCurrencyToBaseSymbol,
                feeCurrencyToBasePrice: feeCurrencyToBasePrice,
                feeCurrencySymbol: feeCurrencySymbol,
                feeCurrencyPrice: feeCurrencyPrice,
                cancellationToken: cancellationToken);

            if (feeCurrencySymbol == null || feeCurrencyPrice == 0)
                return 0m;

            return AmountHelper.RoundDown(feeCurrencySymbol.IsBaseCurrency(Name)
                ? rewardForRedeemInXtz / feeCurrencyPrice
                : rewardForRedeemInXtz * feeCurrencyPrice, DigitsMultiplier);
        }

        public override decimal GetDefaultFee() =>
            TransferGasLimit;
    }
}