using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Microsoft.Extensions.Configuration;

namespace Atomex.Common.Configuration
{
    public static class ConfigurationBuilderExtensions
    {
        public static IConfigurationBuilder AddEmbeddedJsonFile(
            this IConfigurationBuilder cb,
            Assembly assembly,
            string name)
        {
            var resourceNames = assembly.GetManifestResourceNames();

            var fullFileName = resourceNames.FirstOrDefault(n => n.EndsWith(name));

            var stream = assembly.GetManifestResourceStream(fullFileName);

            return cb.AddJsonStream(stream);
        }

        public static IConfigurationBuilder AddJsonString(
            this IConfigurationBuilder cb,
            string json)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

            return cb.AddJsonStream(stream);
        }
    }
}