using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;
using Atomex.Core;
using Atomex.EthereumTokens;
using Atomex.Wallet.Abstract;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.TezosTokens;
using Atomex.Blockchain.Ethereum.Abstract;

namespace Atomex.ViewModels
{
    public enum BalanceErrorType
    {
        FailedToGet,
        LessThanExpected,
        MoreThanExpected
    }

    public class BalanceError
    {
        public BalanceErrorType Type { get; set; }
        public string Address { get; set; }
        public decimal LocalBalance { get; set; }
        public decimal ActualBalance { get; set; }
    }

    public static class BalanceChecker
    {
        public static Task<IEnumerable<BalanceError>> CheckBalancesAsync(
            IAccount account,
            IEnumerable<WalletAddress> addresses,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var errors = new List<BalanceError>();

                    foreach (var address in addresses)
                    {
                        var actualBalance = 0m;

                        if (Currencies.IsTezosToken(address.Currency))
                        {
                            // tezos tokens
                            var xtzConfig = account.Currencies.Get<TezosConfig>("XTZ");
                            var tezosTokenConfig = account.Currencies.Get<TezosTokenConfig>(address.Currency);

                            var tzktApi = new TzktApi(xtzConfig);

                            var balanceResult = await tzktApi
                                .GetTokenBalanceAsync(
                                    addresses: new[] { address.Address },
                                    tokenContracts: new[] { tezosTokenConfig.TokenContractAddress },
                                    tokenIds: new [] { tezosTokenConfig.TokenId },
                                    cancellationToken: cancellationToken)
                                .ConfigureAwait(false);

                            if (balanceResult == null ||
                                balanceResult.HasError ||
                                balanceResult.Value == null ||
                                !balanceResult.Value.Any())
                            {
                                errors.Add(new BalanceError
                                {
                                    Type         = BalanceErrorType.FailedToGet,
                                    Address      = address.Address,
                                    LocalBalance = address.AvailableBalance(),
                                });

                                continue;
                            }

                            actualBalance = balanceResult.Value
                                .First()
                                .GetTokenBalance();
                        }
                        else if (Currencies.IsEthereumToken(address.Currency))
                        {
                            // ethereum tokens
                            var erc20 = account.Currencies
                                .Get<Erc20Config>(address.Currency);

                            var api = erc20.BlockchainApi as IEthereumApi;

                            var balanceResult = await api
                                .TryGetErc20BalanceAsync(
                                    address: address.Address,
                                    contractAddress: erc20.ERC20ContractAddress,
                                    cancellationToken: cancellationToken)
                                .ConfigureAwait(false);

                            if (balanceResult == null || balanceResult.HasError)
                            {
                                errors.Add(new BalanceError
                                {
                                    Type         = BalanceErrorType.FailedToGet,
                                    Address      = address.Address,
                                    LocalBalance = address.AvailableBalance(),
                                });

                                continue;
                            }

                            actualBalance = erc20.TokenDigitsToTokens(balanceResult.Value);
                        }
                        else
                        {
                            var api = account.Currencies
                                .GetByName(address.Currency)
                                .BlockchainApi;

                            var balanceResult = await api
                                .GetBalanceAsync(
                                    address: address.Address,
                                    cancellationToken: cancellationToken)
                                .ConfigureAwait(false);

                            if (balanceResult == null || balanceResult.HasError)
                            {
                                errors.Add(new BalanceError
                                {
                                    Type         = BalanceErrorType.FailedToGet,
                                    Address      = address.Address,
                                    LocalBalance = address.AvailableBalance(),
                                });

                                continue;
                            }

                            actualBalance = balanceResult.Value;
                        }

                        if (actualBalance < address.AvailableBalance())
                        {
                            errors.Add(new BalanceError
                            {
                                Type          = BalanceErrorType.LessThanExpected,
                                Address       = address.Address,
                                LocalBalance  = address.AvailableBalance(),
                                ActualBalance = actualBalance
                            });
                        }
                        else if (actualBalance > address.AvailableBalance() &&
                                 Currencies.IsBitcoinBased(address.Currency))
                        {
                            errors.Add(new BalanceError
                            {
                                Type          = BalanceErrorType.MoreThanExpected,
                                Address       = address.Address,
                                LocalBalance  = address.AvailableBalance(),
                                ActualBalance = actualBalance
                            });
                        }
                    }

                    return errors;
                }
                catch (Exception e)
                {
                    Log.Error(e, "Balance check error");
                }

                return addresses.Select(a => new BalanceError
                {
                    Type         = BalanceErrorType.FailedToGet,
                    Address      = a.Address,
                    LocalBalance = a.AvailableBalance()
                });

            }, cancellationToken);
        }
    }
}