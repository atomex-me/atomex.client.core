using Atomex.Blockchain.Tezos.Abstract;
using Atomex.Common;
using Atomex.Wallets.Abstract;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Wallets.Tezos
{
    public class TezosTokensScanner : WalletScanner<ITezosApi>
    {
        protected override ITezosApi GetBlockchainApi()
        {
            throw new NotImplementedException();
        }

        protected override CurrencyConfig GetCurrencyConfig()
        {
            throw new NotImplementedException();
        }

        protected override Task<(bool hasActivity, Error error)> UpdateAddressBalanceAsync(
            string address,
            string keyPath,
            WalletInfo walletInfo,
            WalletAddress storedAddress,
            ITezosApi api,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}