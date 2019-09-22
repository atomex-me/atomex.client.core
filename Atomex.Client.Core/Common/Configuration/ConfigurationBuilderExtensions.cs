using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Atomex.Common.Configuration
{
    public static class ConfigurationBuilderExtensions
    {
        public static IConfigurationBuilder AddEmbeddedJsonFile(
            this IConfigurationBuilder cb,
            Assembly assembly,
            string name,
            bool optional = false)
        {
            // reload on change is not supported, always pass in false
            return cb.AddJsonFile(new EmbeddedFileProvider(assembly), name, optional, false);
        }

        public static IConfigurationBuilder AddJsonString(
            this IConfigurationBuilder cb,
            string json,
            string name,
            bool optional = false)
        {
            return cb.AddJsonFile(new StringProvider(json), name, optional, false);
        }
    }
}