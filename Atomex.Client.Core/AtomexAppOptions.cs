using Microsoft.Extensions.Configuration;

using Atomex.Swaps;

namespace Atomex
{
    public record AtomexAppOptions
    {
        public SwapManagerOptions SwapManager { get; init; }

        public static AtomexAppOptions Default => new()
        {
            SwapManager = SwapManagerOptions.Default
        };

        public static AtomexAppOptions LoadFromConfiguration(IConfiguration configuration)
        {
            const string USE_WATCH_TOWER_MOVE_PATH = "SwapManager:UseWatchTowerMode";
            const string ALLOW_SPENDING_ALL_OUTPUTS_PATH = "SwapManager:AllowSpendingAllOutputs";

            var options = Default;

            if (bool.TryParse(configuration[USE_WATCH_TOWER_MOVE_PATH], out var useWatchTowerMode))
                options.SwapManager.UseWatchTowerMode = useWatchTowerMode;

            if (bool.TryParse(configuration[ALLOW_SPENDING_ALL_OUTPUTS_PATH], out var allowSpendingAllOutputs))
                options.SwapManager.AllowSpendingAllOutputs = allowSpendingAllOutputs;

            return options;
        }
    }
}