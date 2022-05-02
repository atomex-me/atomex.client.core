using System.Numerics;

using Nethereum.Hex.HexTypes;

namespace Atomex.Blockchain.Ethereum.Messages.Erc20.Swaps.V1
{
    public class Erc20InitiateMessage
    {
        private const int PrefixLength = 2;
        private const int MethodLength = 8;
        private const int ParamOffset = PrefixLength + MethodLength;
        private const int ParamLength = 64;
        private const int InputLength = ParamOffset + 8 * ParamLength;

        public string SecretHash { get; set; }
        public string Erc20Contract { get; set; }
        public string Participant { get; set; }
        public long RefundTimeStamp { get; set; }
        public long Countdown { get; set; }
        public BigInteger Value { get; set; }
        public BigInteger Payoff { get; set; }
        public bool Active { get; set; }

        public static bool TryParse(string input, out Erc20InitiateMessage initiate)
        {
            initiate = null;

            if (input.Length != InputLength)
                return false;

            try
            {
                initiate = new Erc20InitiateMessage
                {
                    SecretHash = input.Substring(ParamOffset, ParamLength),
                    Erc20Contract = "0x" + input.Substring(ParamOffset + ParamLength, ParamLength),
                    Participant = "0x" + input.Substring(ParamOffset + 2 * ParamLength, ParamLength),
                    RefundTimeStamp = (long)new HexBigInteger(input.Substring(ParamOffset + 3 * ParamLength, ParamLength)).Value,
                    Countdown = (long)new HexBigInteger(input.Substring(ParamOffset + 4 * ParamLength, ParamLength)).Value,
                    Value = new HexBigInteger(input.Substring(ParamOffset + 5 * ParamLength, ParamLength)).Value,
                    Payoff = new HexBigInteger(input.Substring(ParamOffset + 6 * ParamLength, ParamLength)).Value,
                    Active = new HexBigInteger(input.Substring(ParamOffset + 7 * ParamLength, ParamLength)).Value > 0
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