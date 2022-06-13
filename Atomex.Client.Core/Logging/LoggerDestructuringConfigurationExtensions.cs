using Serilog;
using Serilog.Configuration;

namespace Atomex.Logging
{
    public static class LoggerDestructuringConfigurationExtensions
    {
        public static LoggerConfiguration WithAtomexDestructuringPolicies(this LoggerDestructuringConfiguration loggerDestructuringConfiguration)
            => loggerDestructuringConfiguration.With(
                new SensitiveDataDestructuringPolicy()
            );
    }
}
