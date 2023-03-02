using System.Numerics;

using Atomex.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet.Tezos
{
    public class Fa2Account : TezosTokenAccount
    {
        public Fa2Account(
            string tokenContract,
            BigInteger tokenId,
            ICurrencies currencies,
            IHdWallet wallet,
            ILocalStorage localStorage,
            TezosAccount tezosAccount)
            : base("FA2",
                  tokenContract,
                  tokenId,
                  currencies,
                  wallet,
                  localStorage,
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
            BigInteger tokenId,
            string from,
            string to,
            BigInteger amount)
        {
            return $"[{{\"prim\":\"Pair\",\"args\":[{{\"string\":\"{from}\"}},[{{\"prim\":\"Pair\",\"args\":[{{\"string\":\"{to}\"}},{{\"prim\":\"Pair\",\"args\":[{{\"int\":\"{tokenId}\",\"int\":\"{amount}\"}}]}}]}}]]}}]";
        }

        #endregion Helpers
    }
}