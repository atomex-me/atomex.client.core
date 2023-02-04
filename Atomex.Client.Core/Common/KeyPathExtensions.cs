using System;

namespace Atomex.Common
{
    public static class KeyPathExtensions
    {
        private const char IndexSeparator = '/';
        private const char HardenedSuffix = '\'';
        public const string PurposePattern = "{p}";
        public const string CoinTypePattern = "{t}";
        public const string AccountPattern = "{a}";
        public const string ChainPattern = "{c}";
        public const string IndexPattern = "{i}";
        public const string ExternalChain = "0";
        public const string InternalChain = "1";
        public const string DefaultAccount = "0'";
        public const string DefaultIndex = "0";

        public static bool IsMatch(this string keyPath, string keyPathPattern)
        {
            var patternParts = keyPathPattern.Split(IndexSeparator);
            var indexes = keyPath.Split(IndexSeparator);

            if (indexes.Length != patternParts.Length)
                return false;

            for (var i = 0; i < patternParts.Length; ++i)
            {
                var pattern = patternParts[i];
                var index = indexes[i];

                if (pattern == index)
                    continue;

                if (pattern.IsHardened() != index.IsHardened())
                    return false;

                if (!index.IsUnsignedInteger())
                    return false;

                if (!pattern.StartsWith(PurposePattern) &&
                    !pattern.StartsWith(CoinTypePattern) &&
                    !pattern.StartsWith(AccountPattern) &&
                    !pattern.StartsWith(ChainPattern) &&
                    !pattern.StartsWith(IndexPattern))
                    return false;
            }

            return true;
        }

        public static string SetIndex(
            this string keyPath,
            string keyPathPattern,
            string indexPattern,
            string indexValue)
        {
            var patternParts = keyPathPattern.Split(IndexSeparator);
            var indexes = keyPath.Split(IndexSeparator);

            for (var i = 0; i < patternParts.Length; ++i)
                if (patternParts[i].StartsWith(indexPattern))
                    indexes[i] = indexValue;

            return string.Join($"{IndexSeparator}", indexes);
        }

        public static uint GetIndex(
            this string keyPath,
            string keyPathPattern,
            string indexPattern)
        {
            var patternParts = keyPathPattern.Split(IndexSeparator);
            var indexes = keyPath.Split(IndexSeparator);

            for (var i = 0; i < patternParts.Length; ++i)
                if (patternParts[i].StartsWith(indexPattern))
                    return uint.Parse(indexes[i].TrimEnd(HardenedSuffix));

            throw new Exception("Key path pattern does not contain index pattern");
        }

        public static bool IsHardened(this string index) =>
            index.EndsWith($"{HardenedSuffix}");

        public static bool IsUnsignedInteger(this string index) =>
            uint.TryParse(index.TrimEnd(HardenedSuffix), out _);
    }
}