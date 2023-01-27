using Atomex.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet.Tezos
{
    public class Fa12Account : TezosTokenAccount
    {
        public Fa12Account(
            string tokenContract,
            int tokenId,
            ICurrencies currencies,
            IHdWallet wallet,
            ILocalStorage dataRepository,
            TezosAccount tezosAccount)
            : base("FA12",
                  tokenContract,
                  tokenId,
                  currencies,
                  wallet,
                  dataRepository,
                  tezosAccount)
        {
        }

        #region Helpers

        protected override string CreateTransferParams(
            string from,
            string to,
            decimal amount)
        {
            return $"{{'prim':'Pair','args':[{{'string':'{from}'}},{{'prim':'Pair','args':[{{'string':'{to}'}},{{'int':'{amount}'}}]}}]}}";
        }

        #endregion Helpers
    }
}