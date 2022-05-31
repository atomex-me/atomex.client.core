using System;
using System.Collections.Generic;
using System.Linq;

using Netezos.Encoding;
using Xunit;

using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Blockchain.Tezos.Operations;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos
{
    public class TzktApiTests
    {
        private TzktApi CreateApi() => new TzktApi(new TzktSettings { BaseUri = TzktApi.Uri });

        public static TezosOperation DelegationWithReveal => new TezosOperation(new Operation[]
        {
            new RevealOperation
            {
                Type       = "reveal",
                Hash       = "ooP37LNma6DiWjVxDbS2XZu4PiNKy7fbHZWSn8Vj8FX1hWfkC3b",
                Block      = "BLwRUPupdhP8TyWp9J6TbjLSCxPPW6tyhVPF2KmNAbLPt7thjPw",
                BlockLevel = 109,
                BlockTime  = DateTimeOffset.Parse("2018-06-30T19:30:27Z"),
                Sender     = new Alias { Address = "tz1Wit2PqodvPeuRRhdQXmkrtU8e8bRYZecd" },
                Counter    = 22,
                GasLimit   = 0,
                BakerFee   = 0,
                GasUsed    = 0,
                Status     = "applied"
            },
            new DelegationOperation
            {
                Type        = "delegation",
                Hash        = "ooP37LNma6DiWjVxDbS2XZu4PiNKy7fbHZWSn8Vj8FX1hWfkC3b",
                Block       = "BLwRUPupdhP8TyWp9J6TbjLSCxPPW6tyhVPF2KmNAbLPt7thjPw",
                BlockLevel  = 109,
                BlockTime   = DateTimeOffset.Parse("2018-06-30T19:30:27Z"),
                Sender      = new Alias { Address = "tz1Wit2PqodvPeuRRhdQXmkrtU8e8bRYZecd" },
                GasLimit    = 0,
                GasUsed     = 0,
                BakerFee    = 50000,
                Amount      = 25079312620,
                NewDelegate = new Alias { Address = "tz1Wit2PqodvPeuRRhdQXmkrtU8e8bRYZecd" },
                Status      = "applied",
                Counter     = 23
            }
        });

        public static TezosOperation Activation => new TezosOperation(new Operation[]
        {
            new ActivationOperation
            {
                Type       = "activation",
                Hash       = "oomgUzsUkTtE4v9bYbfF5hg1LLZxtBdYHn7yzQP8GK7kwRk1W56",
                Block      = "BLGD9HqcQiyKa9eQ8pBuNVPR3bkc3XgaT9BgxLaeE2jdvcpCYEX",
                BlockLevel = 30,
                BlockTime  = DateTimeOffset.Parse("2018-06-30T18:11:27Z"),
                Account    = new Alias { Address = "tz1iTTEtNCQfm2hXqiuDoCQZPEUHF6J5bwDU" },
                Balance    = 2427770280
            }
        });

        public static TezosOperation Transaction => new TezosOperation(new Operation[]
        {
            new TransactionOperation
            {
                Type          = "transaction",
                Hash          = "onwEbW3qNfyDunfNo9CMVgiWDwM4T9zvsNHeNuV9DhaZxqQyftY",
                AllocationFee = 0,
                Amount        = 0,
                BakerFee      = 1673,
                Block         = "BM8fCcBXuUuWGKif11xVCBGm1RNDLvtWtkCiiosdN1AN7fFL6hB",
                BlockLevel    = 1494876,
                BlockTime     = DateTimeOffset.Parse("2021-05-31T05:50:38Z"),
                Counter       = 1561324,
                GasLimit      = 13555,
                GasUsed       = 10601,
                Initiator     = null,
                HasInternals  = true,
                Sender        = new Alias { Address = "tz1aKTCbAUuea2RV9kxqRVRg3HT7f1RKnp6a" },
                Status        = "applied",
                StorageFee    = 0,
                StorageLimit  = 257,
                StorageUsed   = 0,
                Target        = new Alias { Address = "KT1VG2WtYdSWz5E7chTeAdDPZNy2MpP8pTfL" },
                Parameter = new Parameter
                {
                    Entrypoint = "redeem",
                    Value = Micheline.FromJson("{\"bytes\":\"99d93bc287c8cb6dd78da6b547fdec783196d4ba9c37702d6250f2c8265726d6\"}")
                }
            },
            new TransactionOperation
            {
                Type          = "transaction",
                Hash          = "onwEbW3qNfyDunfNo9CMVgiWDwM4T9zvsNHeNuV9DhaZxqQyftY",
                AllocationFee = 64250,
                Amount        = 29137675,
                BakerFee      = 0,
                Block         = "BM8fCcBXuUuWGKif11xVCBGm1RNDLvtWtkCiiosdN1AN7fFL6hB",
                BlockLevel    = 1494876,
                BlockTime     = DateTimeOffset.Parse("2021-05-31T05:50:38Z"),
                Counter       = 1561324,
                GasLimit      = 0,
                GasUsed       = 1427,
                Initiator     = new Alias { Address = "tz1aKTCbAUuea2RV9kxqRVRg3HT7f1RKnp6a" },
                HasInternals  = false,
                Sender        = new Alias{ Address = "KT1VG2WtYdSWz5E7chTeAdDPZNy2MpP8pTfL" },
                Status        = "applied",
                StorageFee    = 0,
                StorageLimit  = 0,
                StorageUsed   = 0,
                Target        = new Alias { Address = "tz1bEykBPzHcRD7tMUhsy5xZ6qZ7Xfc9BWxp" },
                Parameter     = null
            },
            new TransactionOperation
            {
                Type          = "transaction",
                Hash          = "onwEbW3qNfyDunfNo9CMVgiWDwM4T9zvsNHeNuV9DhaZxqQyftY",
                AllocationFee = 0,
                Amount        = 4364,
                BakerFee      = 0,
                Block         = "BM8fCcBXuUuWGKif11xVCBGm1RNDLvtWtkCiiosdN1AN7fFL6hB",
                BlockLevel    = 1494876,
                BlockTime     = DateTimeOffset.Parse("2021-05-31T05:50:38Z"),
                Counter       = 1561324,
                GasLimit      = 0,
                GasUsed       = 1427,
                Initiator     = new Alias { Address = "tz1aKTCbAUuea2RV9kxqRVRg3HT7f1RKnp6a" },
                HasInternals  = false,
                Sender        = new Alias { Address = "KT1VG2WtYdSWz5E7chTeAdDPZNy2MpP8pTfL" },
                Status        = "applied",
                StorageFee    = 0,
                StorageLimit  = 0,
                StorageUsed   = 0,
                Target        = new Alias { Address = "tz1aKTCbAUuea2RV9kxqRVRg3HT7f1RKnp6a" },
                Parameter     = null
            }
        });

        public static IEnumerable<object[]> Operations => new List<object[]>
        {
            new object[] { DelegationWithReveal },
            new object[] { Activation },
            new object[] { Transaction }
        };

        [Theory]
        [InlineData("tz1aKTCbAUuea2RV9kxqRVRg3HT7f1RKnp6a")]
        [InlineData("KT1VG2WtYdSWz5E7chTeAdDPZNy2MpP8pTfL")]
        [HasNetworkRequests()]
        public async void CanGetBalanceAsync(string address)
        {
            var api = CreateApi();

            var (balance, error) = await api.GetBalanceAsync(address);

            Assert.Null(error);
            Assert.True(balance >= 0);
        }

        [Theory]
        [MemberData(nameof(Operations))]
        [HasNetworkRequests()]
        public async void CanGetTransactionAsync(TezosOperation expectedOperation)
        {
            var api = CreateApi();

            var (tx, error) = await api.GetTransactionAsync(expectedOperation.TxId);

            Assert.NotNull(tx);
            Assert.Null(error);

            var tezosOperation = tx as TezosOperation;

            Assert.NotNull(tezosOperation);

            var expectedOps = expectedOperation.Operations.ToList();
            var ops = tezosOperation.Operations.ToList();

            Assert.Equal(expectedOps.Count, ops.Count);

            for (var i = 0; i < expectedOps.Count; ++i)
            {
                IsOperationEquals(expectedOps[i], ops[i]);

                var _ = expectedOps[i].Type switch
                {
                    "transaction" => IsTransactionsEquals(expectedOps[i], ops[i]),
                    "activation"  => IsActivationsEquals(expectedOps[i], ops[i]),
                    "delegation"  => IsDelegationEquals(expectedOps[i], ops[i]),
                    "reveal"      => true, // nothing to do
                    _             => true  // todo: not implemented
                };
            }
        }

        private void IsOperationEquals(Operation expectedOp, Operation op)
        {
            Assert.Equal(expectedOp.Type, op.Type);
            Assert.Equal(expectedOp.Hash, op.Hash);
            Assert.Equal(expectedOp.Block, op.Block);
            Assert.Equal(expectedOp.BlockLevel, op.BlockLevel);
            Assert.Equal(expectedOp.BlockTime, op.BlockTime);
            Assert.Equal(expectedOp.GasUsed, op.GasUsed);
            Assert.Equal(expectedOp.Sender?.Address, op.Sender?.Address);
            Assert.Equal(expectedOp.Status, op.Status);
        }

        private bool IsTransactionsEquals(Operation expectedOperation, Operation operation)
        {
            var eto = expectedOperation as TransactionOperation;
            var op = operation as TransactionOperation;

            Assert.NotNull(eto);
            Assert.NotNull(op);

            Assert.Equal(eto.AllocationFee, op.AllocationFee);
            Assert.Equal(eto.Amount, op.Amount);
            Assert.Equal(eto.BakerFee, op.BakerFee);
            Assert.Equal(eto.Counter, op.Counter);
            Assert.Equal(eto.GasLimit, op.GasLimit);
            Assert.Equal(eto.Initiator?.Address, op.Initiator?.Address);
            Assert.Equal(eto.HasInternals, op.HasInternals);
            Assert.Equal(eto.StorageFee, op.StorageFee);
            Assert.Equal(eto.StorageLimit, op.StorageLimit);
            Assert.Equal(eto.StorageUsed, op.StorageUsed);
            Assert.Equal(eto.Target?.Address, op.Target?.Address);
            Assert.Equal(eto.Parameter?.Entrypoint, op.Parameter?.Entrypoint);
            //Assert.Equal(expectedOp.Parameter?.Value, op.Parameter?.Value);

            return true;
        }

        private bool IsActivationsEquals(Operation expectedOperation, Operation operation)
        {
            var eao = expectedOperation as ActivationOperation;
            var op = operation as ActivationOperation;

            Assert.NotNull(eao);
            Assert.NotNull(op);

            Assert.Equal(eao.Account?.Address, op.Account?.Address);
            Assert.Equal(eao.Balance, op.Balance);

            return true;
        }

        private bool IsDelegationEquals(Operation expectedOperation, Operation operation)
        {
            var edo = expectedOperation as DelegationOperation;
            var op = operation as DelegationOperation;

            Assert.NotNull(edo);
            Assert.NotNull(op);

            Assert.Equal(edo.PrevDelegate?.Address, op.PrevDelegate?.Address);
            Assert.Equal(edo.NewDelegate?.Address, op.NewDelegate?.Address);
            Assert.Equal(edo.Amount, op.Amount);

            return true;
        }
    }
}