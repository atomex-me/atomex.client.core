using System;
using System.Collections.Generic;

using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Util;

namespace Atomex.Blockchain.Ethereum
{
    public static class FunctionSignatureExtractor
    {
        public static string ExtractSignature<T>()
        {
            string result = null;

            var memberInfo = typeof(T);

            var classAttributes = memberInfo.GetCustomAttributes(true);

            foreach (var attribute in classAttributes)
            {
                if (attribute is FunctionAttribute functionAttribute)
                {
                    result += $"{functionAttribute.Name}(";
                    break;
                }
            }

            if (result == null)
                throw new Exception("Can't find event class name");

            var types = new List<string>();

            var classProperties = memberInfo.GetProperties();

            foreach (var property in classProperties)
            {
                var propertyAttributes = property.GetCustomAttributes(true);

                foreach (var attribute in propertyAttributes)
                {
                    if (attribute is ParameterAttribute parameterAttribute)
                        types.Add(parameterAttribute.Type);
                }
            }

            return result + string.Join(",", types) + ")";
        }

        public static string GetSignatureHash<T>(bool withPrefix = true)
        {
            return (withPrefix ? "0x" : "") + Sha3Keccack.Current.CalculateHash(ExtractSignature<T>());
        }
    }
}