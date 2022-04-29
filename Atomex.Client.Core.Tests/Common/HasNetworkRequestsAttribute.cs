using System;
using System.Collections.Generic;

using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atomex.Common
{
    [TraitDiscoverer("Atomex.Common.HasNetworkRequestsDiscoverer", "Atomex.Tests" )]
    [AttributeUsage(AttributeTargets.Method)]
    public class HasNetworkRequestsAttribute : Attribute, ITraitAttribute
    {
        public HasNetworkRequestsAttribute() {}
    }

    public class HasNetworkRequestsDiscoverer : ITraitDiscoverer
    {
        private const string Key = "HasNetworkRequests";
        private const string Value = "True";

        public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
        {
            yield return new KeyValuePair<string, string>(Key, Value);
        }
    }
}