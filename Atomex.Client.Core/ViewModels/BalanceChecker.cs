﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Common;
using Atomex.Wallet.Abstract;
using Atomex.Wallets;

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
        public BigInteger LocalBalance { get; set; }
        public BigInteger ActualBalance { get; set; }
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
                        BigInteger actualBalance = 0;

                        if (Currencies.IsTezosTokenStandard(address.Currency))
                        {
                            // tezos tokens
                            var xtzConfig = account.Currencies.Get<TezosConfig>("XTZ");

                            var tzktApi = new TzktApi(xtzConfig.GetTzktSettings());

                            var (balances, error) = await tzktApi
                                .GetTokenBalanceAsync(
                                    addresses: new[] { address.Address },
                                    tokenContracts: new[] { address.TokenBalance.Contract },
                                    tokenIds: new [] { address.TokenBalance.TokenId},
                                    cancellationToken: cancellationToken)
                                .ConfigureAwait(false);

                            if (error != null ||
                                balances == null ||
                                !balances.Any())
                            {
                                errors.Add(new BalanceError
                                {
                                    Type         = BalanceErrorType.FailedToGet,
                                    Address      = address.Address,
                                    LocalBalance = address.AvailableBalance(),
                                });

                                continue;
                            }

                            actualBalance = balances
                                .First()
                                .GetTokenBalance();
                        }
                        else if (address.Currency == EthereumHelper.Erc20)
                        {
                            // ethereum tokens
                            var ethConfig = account.Currencies.Get<EthereumConfig>("ETH");

                            var api = ethConfig.GetEtherScanApi();

                            var (balance, error) = await api
                                .GetErc20BalanceAsync(
                                    address: address.Address,
                                    tokenContractAddress: address.TokenBalance.Contract,
                                    cancellationToken: cancellationToken)
                                .ConfigureAwait(false);

                            if (error != null)
                            {
                                errors.Add(new BalanceError
                                {
                                    Type         = BalanceErrorType.FailedToGet,
                                    Address      = address.Address,
                                    LocalBalance = address.AvailableBalance(),
                                });

                                continue;
                            }

                            actualBalance = balance;
                        }
                        else
                        {
                            var api = account.Currencies
                                .GetByName(address.Currency)
                                .GetBlockchainApi();

                            var (balance, error) = await api
                                .GetBalanceAsync(
                                    address: address.Address,
                                    cancellationToken: cancellationToken)
                                .ConfigureAwait(false);

                            if (error != null)
                            {
                                errors.Add(new BalanceError
                                {
                                    Type         = BalanceErrorType.FailedToGet,
                                    Address      = address.Address,
                                    LocalBalance = address.AvailableBalance(),
                                });

                                continue;
                            }

                            actualBalance = balance;
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