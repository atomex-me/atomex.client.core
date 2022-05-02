namespace Atomex.Blockchain.Bitcoin.Common
{
    public static class IntExtensions
    {
        public static int CompactSize(this int value)
        {
            if (value >= 0 && value <= 252)
                return 1;

            if (value >= 253 && value <= 0xFFFF)
                return 3;

            return 5;
        }
    }
}