namespace Atomex.TzktEvents.Models
{
    public class TezosTokenEvent
    {
        public string Standard = string.Empty;
        public string Contract = string.Empty;
        public decimal TokenId = 0;
        public string Token = string.Empty;
        public string Address;

        public TezosTokenEvent(string address)
        {
            Address = address;
        }

        public TezosTokenEvent(string standard, string contract, decimal tokenId, string token, string address) : this(address)
        {
            Standard = standard;
            Contract = contract;
            TokenId = tokenId;
            Token = token;
        }
    }
}
