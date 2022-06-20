using Atomex.Core;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Atomex.Logging
{
    public class SensitiveDataDestructuringPolicy : IDestructuringPolicy
    {
        private const string HIDDEN_PROPERTY_MASK = "***";

        private static readonly IReadOnlyDictionary<Type, ISet<string>> sensitiveDataPropertyNames = new Dictionary<Type, ISet<string>>()
        {
            [typeof(Swap)] = new HashSet<string>() {
                nameof(Swap.Secret)
            }
        };
        private static readonly ConcurrentDictionary<Type, IEnumerable<(bool hidden, PropertyInfo propertyInfo)>> loggedPropertyInfos = new();

        public bool TryDestructure(
            object value,
            ILogEventPropertyValueFactory propertyValueFactory,
            out LogEventPropertyValue result
        )
        {
            var valueType = value.GetType();
            if (!sensitiveDataPropertyNames.ContainsKey(valueType))
            {
                result = null;
                return false;
            }

            var logEventProperties = new List<LogEventProperty>();
            var loggedPropertyInfos = GetLoggedPropertyInfos(valueType);
            foreach (var (isHidden, propertyInfo) in loggedPropertyInfos)
            {
                var propertyValue = isHidden ? HIDDEN_PROPERTY_MASK : propertyInfo.GetValue(value);

                logEventProperties.Add(new LogEventProperty(propertyInfo.Name, propertyValueFactory.CreatePropertyValue(propertyValue, true)));
            }

            result = new StructureValue(logEventProperties);
            return true;
        }

        private static IEnumerable<(bool isHidden, PropertyInfo propertyInfo)> GetLoggedPropertyInfos(Type valueType)
        {
            if (loggedPropertyInfos.TryGetValue(valueType, out var result))
                return result;

            result = valueType.GetTypeInfo().DeclaredProperties
                .Where(p => p.CanRead && p.GetMethod.IsPublic && !p.GetMethod.IsStatic && p.GetIndexParameters().Length == 0)
                .Select(p => (
                    isHidden: sensitiveDataPropertyNames.TryGetValue(valueType, out var sensitiveProperties)
                        && sensitiveProperties.Contains(p.Name),
                    propertyInfo: p
                ));
            loggedPropertyInfos.TryAdd(valueType, result);

            return result;
        }
    }
}
