using Newtonsoft.Json.Linq;

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

        protected override JObject CreateTransferParams(
            string from,
            string to,
            decimal amount)
        {
            return CreateTransferParams(_tokenId, from, to, amount);
        }

        private JObject CreateTransferParams(
            int tokenId,
            string from,
            string to,
            decimal amount)
        {
            return JObject.FromObject(new
            {
                entrypoint = "transfer",
                value = new object[]
                {
                    new
                    {
                        prim = "Pair",
                        args = new object[]
                        {
                            new
                            {
                                @string = from
                            },
                            new object[]
                            {
                                new
                                {
                                    prim = "Pair",
                                    args = new object[]
                                    {
                                        new
                                        {
                                            @string = to,
                                        },
                                        new
                                        {
                                            prim = "Pair",
                                            args = new object[]
                                            {
                                                new
                                                {
                                                    @int = tokenId.ToString()
                                                },
                                                new
                                                {
                                                    @int = string.Format("{0:0}", amount)
        }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            });
        }

        #endregion Helpers
    }
}