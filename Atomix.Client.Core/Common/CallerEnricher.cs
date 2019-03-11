using System.Diagnostics;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Atomix.Common
{
    public class CallerEnricher : ILogEventEnricher
    {
        private const string CallerPropertyName = "Caller";
        private const string ClassPropertyName = "Class";

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var skip = 3;

            while (true)
            {
                var stack = new StackFrame(skip);
                if (!stack.HasMethod()) {
                    logEvent.AddPropertyIfAbsent(new LogEventProperty(CallerPropertyName, new ScalarValue("<unknown method>")));
                    logEvent.AddPropertyIfAbsent(new LogEventProperty(ClassPropertyName, new ScalarValue("<unknown class>")));
                    return;
                }

                var method = stack.GetMethod();
                if (method.DeclaringType != null && method.DeclaringType.Assembly != typeof(Log).Assembly)
                {
                    var methodName = method.Name;
                    var className = method.DeclaringType.FullName;

                    var shortClassName = method.DeclaringType.DeclaringType != null
                        ? method.DeclaringType.DeclaringType.Name
                        : method.DeclaringType.Name;
                    
                    var caller = $"{className}.{methodName}";

                    logEvent.AddPropertyIfAbsent(new LogEventProperty(CallerPropertyName, new ScalarValue(caller)));
                    logEvent.AddPropertyIfAbsent(new LogEventProperty(ClassPropertyName, new ScalarValue(shortClassName)));
                    return;
                }

                skip++;
            }
        }
    }

    public static class LoggerCallerEnrichmentConfiguration
    {
        public static LoggerConfiguration WithCaller(this LoggerEnrichmentConfiguration enrichmentConfiguration)
        {
            return enrichmentConfiguration.With<CallerEnricher>();
        }
    }
}