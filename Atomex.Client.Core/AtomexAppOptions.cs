using Microsoft.Extensions.Configuration;

using Atomex.Swaps;

namespace Atomex;

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

        if (!bool.TryParse(configuration[USE_WATCH_TOWER_MOVE_PATH], out var useWatchTowerMode))
            return Default;
        
        return Default with
        {
            SwapManager = SwapManagerOptions.Default with
            {
                UseWatchTowerMode = useWatchTowerMode
            }
        };
    }
}