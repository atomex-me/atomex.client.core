namespace Atomex.Blockchain.Ethereum.Messages.Erc20.Swaps.V1
{
    public class Erc20RedeemMessage
    {
        private const int PrefixLength = 2;
        private const int MethodLength = 8;
        private const int ParamOffset = PrefixLength + MethodLength;
        private const int ParamLength = 64;
        private const int InputLength = ParamOffset + 2 * ParamLength;

        public string SecretHash { get; set; }
        public string Secret { get; set; }

        public static bool TryParse(string input, out Erc20RedeemMessage initiate)
        {
            initiate = null;

            if (input.Length != InputLength)
                return false;

            try
            {
                initiate = new Erc20RedeemMessage
                {
                    SecretHash = input.Substring(ParamOffset, ParamLength),
                    Secret = input.Substring(ParamOffset + ParamLength, ParamLength),
                };

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}