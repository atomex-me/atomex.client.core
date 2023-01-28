using System.Numerics;

using Atomex.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet.Tezos
{
    public class Fa2Account : TezosTokenAccount
    {
        public Fa2Account(
            string tokenContract,
            int tokenId,
            ICurrencies currencies,
            IHdWallet wallet,
            ILocalStorage dataRepository,
            TezosAccount tezosAccount)
            : base("FA2",
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
            BigInteger amount)
        {
            return CreateTransferParams(_tokenId, from, to, amount);
        }

        private string CreateTransferParams(
            int tokenId,
            string from,
            string to,
            BigInteger amount)
        {
            return $"[{{\"prim\":\"Pair\",\"args\":[{{\"string\":\"{from}\"}},[{{\"prim\":\"Pair\",\"args\":[{{\"string\":\"{to}\"}},{{\"prim\":\"Pair\",\"args\":[{{\"int\":\"{tokenId}\",\"int\":\"{amount}\"}}]}}]}}]]}}]";
        }

        #endregion Helpers
    }
}