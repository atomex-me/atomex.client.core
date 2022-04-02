using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet.Tezos
{
    public abstract class TezosTokenAccount : ICurrencyAccount
    {
        public event EventHandler<CurrencyEventArgs> BalanceUpdated;

        protected readonly string _tokenContract;
        protected readonly decimal _tokenId;
        protected readonly TezosAccount _tezosAccount;

        public string Currency { get; }
        public string TokenType { get; }
        public ICurrencies Currencies { get; }
        public IHdWallet Wallet { get; }
        public IAccountDataRepository DataRepository { get; }

        protected decimal Balance { get; set; }
        protected decimal UnconfirmedIncome { get; set; }
        protected decimal UnconfirmedOutcome { get; set; }
        protected TezosConfig XtzConfig => Currencies.Get<TezosConfig>(TezosConfig.Xtz);

        public TezosTokenAccount(
            string currency,
            string tokenType,
            string tokenContract,
            decimal tokenId,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository,
            TezosAccount tezosAccount)
        {
            Currency       = currency ?? throw new ArgumentNullException(nameof(currency));
            TokenType      = tokenType ?? throw new ArgumentNullException(nameof(tokenType));
            Currencies     = currencies ?? throw new ArgumentNullException(nameof(currencies));
            Wallet         = wallet ?? throw new ArgumentNullException(nameof(wallet));
            DataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));

            _tokenContract = tokenContract ?? throw new ArgumentNullException(nameof(tokenContract));
            _tokenId       = tokenId;
            _tezosAccount  = tezosAccount ?? throw new ArgumentNullException(nameof(tezosAccount));

            ReloadBalances();
        }

        public abstract Task<(decimal fee, bool isEnougth)> EstimateTransferFeeAsync(
            string from,
            CancellationToken cancellationToken = default);

        #region Balances

        public virtual async Task<Balance> GetAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await DataRepository
                .GetTezosTokenAddressAsync(TokenType, _tokenContract, _tokenId, address)
                .ConfigureAwait(false);

            return walletAddress != null
                ? new Balance(
                    walletAddress.Balance,
                    walletAddress.UnconfirmedIncome,
                    walletAddress.UnconfirmedOutcome)
                : new Balance();
        }

        public virtual Balance GetBalance()
        {
            return new Balance(
                Balance,
                UnconfirmedIncome,
                UnconfirmedOutcome);
        }

        public Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var scanner = new TezosTokensScanner(_tezosAccount);

                await scanner
                    .ScanContractAsync(_tokenContract, cancellationToken)
                    .ConfigureAwait(false);

                ReloadBalances();

            }, cancellationToken);
        }

        public Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var scanner = new TezosTokensScanner(_tezosAccount);

                await scanner
                    .ScanContractAsync(address, _tokenContract, cancellationToken)
                    .ConfigureAwait(false);

                ReloadBalances();

            }, cancellationToken);
        }

        public void ReloadBalances()
        {
            Balance            = 0;
            UnconfirmedIncome  = 0;
            UnconfirmedOutcome = 0;

            var addresses = DataRepository
                .GetUnspentTezosTokenAddressesAsync(TokenType, _tokenContract, _tokenId)
                .WaitForResult();

            foreach (var address in addresses)
            {
                Balance            += address.Balance;
                UnconfirmedIncome  += address.UnconfirmedIncome;
                UnconfirmedOutcome += address.UnconfirmedOutcome;
            }

            BalanceUpdated?.Invoke(this, new CurrencyEventArgs(Currency));
        }

        #endregion Balances

        #region Addresses

        public Task<WalletAddress> DivideAddressAsync(
            KeyIndex keyIndex,
            int keyType)
        {
            return DivideAddressAsync(
                account: keyIndex.Account,
                chain: keyIndex.Chain,
                index: keyIndex.Index,
                keyType: keyType);
        }

        public async Task<WalletAddress> DivideAddressAsync(
            uint account,
            uint chain,
            uint index,
            int keyType)
        {
            var currency = Currencies.GetByName(Currency);

            var walletAddress = Wallet.GetAddress(
                currency: currency,
                account: account,
                chain: chain,
                index: index,
                keyType: keyType);

            if (walletAddress == null)
                return null;

            walletAddress.TokenBalance = new TokenBalance
            {
                Token = new Token()
                {
                    Contract = _tokenContract,
                    TokenId  = _tokenId,
                }
            };

            await DataRepository
                .TryInsertTezosTokenAddressAsync(walletAddress)
                .ConfigureAwait(false);

            return walletAddress;
        }

        public async Task<WalletAddress> GetAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await DataRepository
                .GetTezosTokenAddressAsync(TokenType, _tokenContract, _tokenId, address)
                .ConfigureAwait(false);

            return walletAddress?.ResolvePublicKey(Currencies, Wallet);
        }

        public Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return DataRepository
                .GetTezosTokenAddressesByContractAsync(_tokenContract);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return DataRepository
                .GetUnspentTezosTokenAddressesAsync(TokenType, _tokenContract, _tokenId);
        }

        public async Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            // 1. try to find address with tokens
            var unspentAddresses = await DataRepository
                .GetUnspentTezosTokenAddressesAsync(TokenType, _tokenContract, _tokenId)
                .ConfigureAwait(false);

            if (unspentAddresses.Any())
                return unspentAddresses
                    .MaxBy(w => w.AvailableBalance())
                    .ResolvePublicKey(Currencies, Wallet);

            // 2. try to find xtz address with max balance
            var unspentTezosAddresses = await DataRepository
                .GetUnspentAddressesAsync(TezosConfig.Xtz)
                .ConfigureAwait(false);

            if (unspentTezosAddresses.Any())
            {
                var xtzAddress = unspentTezosAddresses.MaxBy(a => a.AvailableBalance());

                var tokenAddress = await DataRepository
                    .GetTezosTokenAddressAsync(
                        currency: TokenType,
                        tokenContract: _tokenContract,
                        tokenId: _tokenId,
                        address: xtzAddress.Address)
                    .ConfigureAwait(false);

                if (tokenAddress != null)
                    return tokenAddress.ResolvePublicKey(Currencies, Wallet);

                return await DivideAddressAsync(
                        keyIndex: xtzAddress.KeyIndex,
                        keyType: xtzAddress.KeyType)
                    .ConfigureAwait(false);
            }

            // 3. use xtz redeem address
            var xtzRedeemAddress = await _tezosAccount
                .GetRedeemAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            var tokenRedeemAddress = await DataRepository
                .GetTezosTokenAddressAsync(
                    currency: TokenType,
                    tokenContract: _tokenContract,
                    tokenId: _tokenId,
                    address: xtzRedeemAddress.Address)
                .ConfigureAwait(false);

            if (tokenRedeemAddress != null)
                return tokenRedeemAddress.ResolvePublicKey(Currencies, Wallet);

            return await DivideAddressAsync(
                    keyIndex: xtzRedeemAddress.KeyIndex,
                    keyType: xtzRedeemAddress.KeyType)
                .ConfigureAwait(false);
        }

        #endregion Addresses
    }
}