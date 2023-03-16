using System.Numerics;

using Atomex.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet.Tezos
{
    public class Fa12Account : TezosTokenAccount
    {
        public Fa12Account(
            string tokenContract,
            BigInteger tokenId,
            ICurrencies currencies,
            IHdWallet wallet,
            ILocalStorage localStorage,
            TezosAccount tezosAccount)
            : base(TezosHelper.Fa12,
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
            return $"{{\"prim\":\"Pair\",\"args\":[{{\"string\":\"{from}\"}},{{\"prim\":\"Pair\",\"args\":[{{\"string\":\"{to}\"}},{{\"int\":\"{amount}\"}}]}}]}}";
        }

        #endregion Helpers
    }
}