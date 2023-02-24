using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using LiteDB;
using NBitcoin;

using Atomex.Blockchain;
using Atomex.Blockchain.Bitcoin;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Erc20;
using Atomex.Blockchain.Tezos;
using Atomex.Client.Entities;
using Atomex.Common;
using Atomex.Common.Bson;
using Atomex.Core;
using Atomex.Wallets.Bips;
using Network = Atomex.Core.Network;
using Swap = Atomex.Core.Swap;
using SwapStatus = Atomex.Client.V1.Entities.SwapStatus;

namespace Atomex.LiteDb.Migrations
{
    public class LiteDbMigration_11_to_12
    {
        private const string Temp = "temp";
        private const string Backup = "backup";
        private const string TxIdKey = "TxId";
        private const string OrderIdKey = "OrderId";

        public static LiteDbMigrationResult Migrate(
            string pathToDb,
            string sessionPassword,
            Network network)
        {
            var oldConnectionString = $"FileName={pathToDb};Password={sessionPassword};Connection=direct;Upgrade=true";
            var newConnectionString = $"FileName={pathToDb}.{Temp};Password={sessionPassword};Connection=direct";

            var mapper = CreateBsonMapper(network);

            using (var oldDb = new LiteDatabase(oldConnectionString))
            using (var newDb = new LiteDatabase(newConnectionString, mapper))
            {
                // create transactions metadata collection
                const string InitialId = "0";

                var metadata = newDb.GetCollection<TransactionMetadata>("TransactionsMetadata");
                metadata.Insert(new TransactionMetadata { Id = InitialId });
                metadata.Delete(InitialId);

                // wallet addresses
                foreach (var oldAddress in oldDb.GetCollection("Addresses").FindAll())
                {
                    var currency   = oldAddress["Currency"].AsString;
                    var keyAccount = (uint)oldAddress["KeyIndex"]["Account"].AsInt32;
                    var keyChain   = (uint)oldAddress["KeyIndex"]["Chain"].AsInt32;
                    var keyIndex   = (uint)oldAddress["KeyIndex"]["Index"].AsInt32;
                    var keyType    = oldAddress["KeyType"].AsInt32;

                    var usageType = currency == "BTC" || currency == "LTC"
                        ? WalletAddressUsageType.SingleUse
                        : WalletAddressUsageType.InUse;

                    var decimals = currency switch
                    {
                        "BTC" or "LTC" or "WBTC" => 8,
                        "ETH" => 18,
                        "XTZ" => 6,
                        "USDT" => 6,
                        "TBTC" => 18,
                        _ => -1
                    };

                    if (decimals == -1)
                    {
                        // skip wallet address with invalid currency
                        continue;
                    }

                    const int StandardKey = 0;
                    const int Bip32Ed25519Key = 1;

                    var keyPath = (currency, keyType) switch
                    {
                        ("BTC", StandardKey)     => $"m/{Bip44.Purpose}'/{Bip44.Bitcoin}'/0'/{keyChain}/{keyIndex}",
                        ("LTC", StandardKey)     => $"m/{Bip44.Purpose}'/{Bip44.Litecoin}'/0'/{keyChain}/{keyIndex}",
                        ("ETH", StandardKey)     => $"m/{Bip44.Purpose}'/{Bip44.Ethereum}'/0'/{keyChain}/{keyIndex}",
                        ("XTZ", StandardKey)     => $"m/{Bip44.Purpose}'/{Bip44.Tezos}'/{keyAccount}'/{keyChain}'",
                        ("XTZ", Bip32Ed25519Key) => $"m/{Bip44.Purpose}'/{Bip44.Tezos}'/0'/{keyChain}/{keyIndex}",
                        ("USDT", StandardKey)    => $"m/{Bip44.Purpose}'/{Bip44.Ethereum}'/0'/{keyChain}/{keyIndex}",
                        ("WBTC", StandardKey)    => $"m/{Bip44.Purpose}'/{Bip44.Ethereum}'/0'/{keyChain}/{keyIndex}",
                        ("TBTC", StandardKey)    => $"m/{Bip44.Purpose}'/{Bip44.Ethereum}'/0'/{keyChain}/{keyIndex}",
                        _ => throw new Exception($"Invalid currency {currency} or key type {keyType}")
                    };

                    var walletAddress = new WalletAddress
                    {
                        Address               = oldAddress["Address"].AsString,
                        Currency              = oldAddress["Currency"].AsString,
                        Balance               = oldAddress["Balance"].AsDecimal.ToBigInteger(decimals),
                        UnconfirmedIncome     = oldAddress["UnconfirmedIncome"].AsDecimal.ToBigInteger(decimals),
                        UnconfirmedOutcome    = oldAddress["UnconfirmedOutcome"].AsDecimal.ToBigInteger(decimals),
                        HasActivity           = oldAddress["HasActivity"].AsBoolean,
                        KeyIndex              = keyIndex,
                        KeyPath               = keyPath,
                        KeyType               = keyType,
                        LastSuccessfullUpdate = DateTime.MinValue,
                        UsageType             = usageType,
                        TokenBalance          = null
                    };

                    var walletAddressDocument = mapper.ToDocument(walletAddress);

                    _ = newDb
                        .GetCollection("Addresses")
                        .Upsert(walletAddressDocument);
                }

                // bitcoin based outputs
                foreach (var oldOutput in oldDb.GetCollection("Outputs").FindAll())
                {
                    var output = ToOutput(oldOutput, oldDb);

                    var outputDocument = mapper.ToDocument(output);

                    _ = newDb
                        .GetCollection("Outputs")
                        .Upsert(outputDocument);
                }

                // bitcoin based transactions
                foreach (var oldTx in oldDb.GetCollection("Transactions").FindAll())
                {
                    var currency = oldTx["Currency"].AsString;

                    // skip all transaction except bitcoin based
                    if (currency != "BTC" && currency != "LTC")
                        continue;

                    var creationTime = oldTx.ContainsKey("CreationTime") && !oldTx["CreationTime"].IsNull
                        ? oldTx["CreationTime"].AsDateTime
                        : (DateTime?)null;

                    var blockInfo = oldTx.ContainsKey("BlockInfo") && !oldTx["BlockInfo"].IsNull
                        ? oldTx["BlockInfo"].AsDocument
                        : null;

                    var blockTime = blockInfo != null && blockInfo.ContainsKey("BlockTime") && !blockInfo["BlockTime"].IsNull
                        ? blockInfo["BlockTime"].AsDateTime
                        : (DateTime?)null;

                    var blockHeight = blockInfo != null && blockInfo.ContainsKey("BlockHeight") && !blockInfo["BlockHeight"].IsNull
                        ? blockInfo["BlockHeight"].AsInt64
                        : 0;

                    var confirmations = blockInfo != null && blockInfo.ContainsKey("Confirmations") && !blockInfo["Confirmations"].IsNull
                        ? blockInfo["Confirmations"].AsInt32
                        : 0;

                    var fee = oldTx.ContainsKey("Fees") && !oldTx["Fees"].IsNull
                        ? oldTx["Fees"].AsInt64
                        : 0;

                    var txNetwork = BitcoinNetworkResolver.ResolveNetwork(currency, network);

                    var tx = new BitcoinTransaction(
                        currency: currency,
                        tx: Transaction.Parse(oldTx["Tx"].AsString, txNetwork),
                        creationTime: creationTime,
                        blockTime: blockTime,
                        blockHeight: blockHeight,
                        confirmations: confirmations,
                        fee: fee);

                    var txDocument = mapper.ToDocument(tx);

                    txDocument["UserMetadata"] = new BsonDocument
                    {
                        ["$id"] = txDocument["_id"],
                        ["$ref"] = "TransactionsMetadata",
                    };

                    _ = newDb
                        .GetCollection("Transactions")
                        .Upsert(txDocument);
                }

                // orders
                foreach (var oldOrder in oldDb.GetCollection("Orders").FindAll())
                {
                    var fromOutputs = oldOrder.ContainsKey("FromOutputs") && !oldOrder["FromOutputs"].IsNull
                        ? oldOrder["FromOutputs"]
                            .AsArray
                            .Select(v => ToOutput(v.AsDocument, oldDb))
                            .Select(o => new BitcoinTxPoint { Hash = o.TxId, Index = o.Index })
                            .ToList()
                        : null;

                    var order = new Order
                    {
                        ClientOrderId     = oldOrder["_id"].AsString,
                        FromAddress       = oldOrder["FromAddress"].AsString,
                        FromOutputs       = fromOutputs,
                        Id                = oldOrder["OrderId"].AsInt64,
                        IsAlreadyCanceled = oldOrder["IsAlreadyCanceled"].AsBoolean,
                        IsApproved        = oldOrder["IsApproved"].AsBoolean,
                        LastPrice         = oldOrder["LastPrice"].AsDecimal,
                        LastQty           = oldOrder["LastQty"].AsDecimal,
                        LeaveQty          = oldOrder["LeaveQty"].AsDecimal,
                        MakerNetworkFee   = oldOrder["MakerNetworkFee"].AsDecimal,
                        Price             = oldOrder["Price"].AsDecimal,
                        Qty               = oldOrder["Qty"].AsDecimal,
                        RedeemFromAddress = oldOrder["RedeemFromAddress"].AsString,
                        Side              = Enum.Parse<Side>(oldOrder["Side"].AsString, ignoreCase: true),
                        Status            = Enum.Parse<OrderStatus>(oldOrder["Status"].AsString, ignoreCase: true),
                        Symbol            = oldOrder["Symbol"].AsString,
                        TimeStamp         = oldOrder["TimeStamp"].AsDateTime,
                        ToAddress         = oldOrder["Symbol"].AsString,
                        Type              = Enum.Parse<OrderType>(oldOrder["Type"].AsString, ignoreCase: true)
                    };

                    var orderDocument = mapper.ToDocument(order);

                    _ = newDb
                        .GetCollection("Orders")
                        .Upsert(orderDocument);
                }

                // swaps
                foreach (var oldSwap in oldDb.GetCollection("Swaps").FindAll())
                {
                    var fromOutputs = oldSwap.ContainsKey("FromOutputs") && !oldSwap["FromOutputs"].IsNull
                        ? oldSwap["FromOutputs"]
                            .AsArray
                            .Select(v => ToOutput(v.AsDocument, oldDb))
                            .Select(o => new BitcoinTxPoint { Hash = o.TxId, Index = o.Index })
                            .ToList()
                        : null;

                    var paymentTxId = oldSwap["PaymentTxId"].AsString;

                    if (paymentTxId == null && oldSwap.ContainsKey("PaymentTx") && !oldSwap["PaymentTx"].IsNull)
                    {
                        paymentTxId = oldSwap["PaymentTx"]["TxId"].AsString;
                        paymentTxId ??= oldSwap["PaymentTx"]["_id"].AsString;
                    }

                    var redeemTxId = oldSwap.ContainsKey("RedeemTxId") && !oldSwap["RedeemTxId"].IsNull
                        ? oldSwap["RedeemTxId"]["TxId"].AsString
                        : null;

                    if (redeemTxId == null && oldSwap.ContainsKey("RedeemTx") && !oldSwap["RedeemTx"].IsNull)
                    {
                        redeemTxId = oldSwap["RedeemTx"]["TxId"].AsString;
                        redeemTxId ??= oldSwap["RedeemTx"]["_id"].AsString;
                    }

                    var refundTxId = oldSwap.ContainsKey("RefundTxId") && !oldSwap["RefundTxId"].IsNull
                        ? oldSwap["RefundTxId"]["TxId"].AsString
                        : null;

                    if (refundTxId == null && oldSwap.ContainsKey("RefundTx") && !oldSwap["RefundTx"].IsNull)
                    {
                        refundTxId = oldSwap["RefundTx"]["TxId"].AsString;
                        refundTxId ??= oldSwap["RefundTx"]["_id"].AsString;
                    }

                    var oldStatus = string.Join(',', oldSwap["Status"].AsString
                        .Split(',')
                        .Intersect(new string[] {
                            "Empty",
                            "Initiated",
                            "Accepted",
                        }));

                    var swap = new Swap
                    {
                        FromAddress            = oldSwap["FromAddress"].AsString,
                        FromOutputs            = fromOutputs,
                        Id                     = oldSwap["_id"].AsInt64,
                        IsInitiator            = oldSwap["IsInitiative"].AsBoolean,
                        LastRedeemTryTimeStamp = DateTime.MinValue,
                        LastRefundTryTimeStamp = DateTime.MinValue,
                        MakerNetworkFee        = oldSwap["MakerNetworkFee"].AsDecimal,
                        OrderId                = oldSwap["OrderId"].AsInt64,
                        PartyAddress           = oldSwap["PartyAddress"].AsString,
                        PartyPaymentTxId       = oldSwap["PartyPaymentTxId"].AsString,
                        PartyRedeemScript      = oldSwap["PartyRedeemScript"].AsString,
                        PartyRefundAddress     = oldSwap["PartyRefundAddress"].AsString,
                        PartyRewardForRedeem   = oldSwap["PartyRewardForRedeem"].AsDecimal,
                        PaymentTxId            = paymentTxId,
                        Price                  = oldSwap["Price"].AsDecimal,
                        Qty                    = oldSwap["Qty"].AsDecimal,
                        RedeemFromAddress      = oldSwap["RedeemFromAddress"].AsString,
                        RedeemScript           = oldSwap["RedeemScript"].AsString,
                        RedeemTxId             = redeemTxId,
                        RefundAddress          = oldSwap["RefundAddress"].AsString,
                        RefundTxId             = refundTxId,
                        RewardForRedeem        = oldSwap["RewardForRedeem"].AsDecimal,
                        Secret                 = oldSwap["Secret"].AsBinary,
                        SecretHash             = oldSwap["SecretHash"].AsBinary,
                        Side                   = Enum.Parse<Side>(oldSwap["Side"].AsString, ignoreCase: true),
                        StateFlags             = Enum.Parse<SwapStateFlags>(oldSwap["StateFlags"].AsString, ignoreCase: true),
                        Status                 = Enum.Parse<SwapStatus>(oldStatus, ignoreCase: true),
                        Symbol                 = oldSwap["Symbol"].AsString,
                        TimeStamp              = oldSwap["TimeStamp"].AsDateTime,
                        ToAddress              = oldSwap["ToAddress"].AsString
                    };

                    var swapDocument = mapper.ToDocument(swap);

                    _ = newDb
                        .GetCollection("Swaps")
                        .Upsert(swapDocument);
                }

                newDb.UserVersion = LiteDbMigrationManager.Version12;
            };

            File.Move(pathToDb, $"{pathToDb}.{Backup}");
            File.Move($"{pathToDb}.{Temp}", pathToDb);

            return new LiteDbMigrationResult
            {
                { Collections.Transactions, "XTZ" },
                { Collections.Transactions, "ETH" },
                { Collections.Transactions, "USDT" },
                { Collections.Transactions, "WBTC" },
                { Collections.Transactions, "TBTC" },
                { Collections.TezosTokensTransfers, "ALL" },
                { Collections.TezosTokensAddresses, "ALL" },
                { Collections.TezosTokensContracts, "ALL" },
            };
        }

        public static BsonMapper CreateBsonMapper(Network network)
        {
            var mapper = new BsonMapper()
                .UseSerializer(new BigIntegerToBsonSerializer())
                .UseSerializer(new JObjectToBsonSerializer())
                .UseSerializer(new CoinToBsonSerializer())
                .UseSerializer(new BitcoinTransactionSerializer(network));

            mapper.Entity<WalletAddress>()
                .Id(w => w.UniqueId)
                .Ignore(w => w.IsDisabled);

            mapper.Entity<BitcoinTxOutput>()
                .Id(o => o.UniqueId)
                .Ignore(o => o.Index)
                .Ignore(o => o.Value)
                .Ignore(o => o.IsValid)
                .Ignore(o => o.TxId)
                .Ignore(o => o.Type)
                .Ignore(o => o.IsSpent)
                .Ignore(o => o.IsP2Sh)
                .Ignore(o => o.IsSegWit);

            mapper.Entity<EthereumTransaction>()
                .Id(t => t.UniqueId)
                .Field(t => t.Id, TxIdKey)
                .Ignore(t => t.IsConfirmed);

            mapper.Entity<TezosOperation>()
                .Id(t => t.UniqueId)
                .Field(t => t.Id, TxIdKey)
                .Ignore(t => t.From)
                .Ignore(t => t.IsConfirmed);

            mapper.Entity<TezosTokenTransfer>()
                .Id(t => t.UniqueId)
                .Field(t => t.Id, TxIdKey);

            mapper.Entity<Erc20Transaction>()
                .Id(t => t.UniqueId)
                .Field(t => t.Id, TxIdKey)
                .Ignore(t => t.IsConfirmed);

            mapper.Entity<TransactionMetadata>()
                .Id(t => t.UniqueId)
                .Field(t => t.Id, TxIdKey);

            mapper.Entity<Order>()
                .Id(o => o.ClientOrderId)
                .Field(o => o.Id, OrderIdKey);

            mapper.Entity<Swap>()
                .Id(s => s.Id)
                .Ignore(s => s.SoldCurrency)
                .Ignore(s => s.PurchasedCurrency)
                .Ignore(s => s.IsComplete)
                .Ignore(s => s.IsRefunded)
                .Ignore(s => s.IsCanceled)
                .Ignore(s => s.IsUnsettled)
                .Ignore(s => s.IsActive)
                .Ignore(s => s.IsAcceptor)
                .Ignore(s => s.HasPartyPayment);

            return mapper;
        }

        private static BitcoinTxOutput ToOutput(BsonDocument document, LiteDatabase db)
        {
            var currency = document["Currency"].AsString;
            var txId = document["TxId"].AsString;

            var spentHash = document.ContainsKey("SpentHash") && !document["SpentHash"].IsNull
                ? document["SpentHash"].AsString
                : null;

            var spentIndex = document["SpentIndex"].AsInt32;

            var tx = db
                .GetCollection("Transactions")
                .FindById($"{txId}:{currency}");

            var isConfirmed = tx != null && tx.ContainsKey("BlockInfo") && !tx["BlockInfo"].IsNull;

            var spentTx = spentHash != null
                ? db.GetCollection("Transactions")
                    .FindById($"{spentHash}:{currency}")
                : null;

            var isSpentConfirmed = spentTx != null && spentTx.ContainsKey("BlockInfo") && !spentTx["BlockInfo"].IsNull;

            return new BitcoinTxOutput
            {
                Coin = new Coin(
                    fromTxHash: uint256.Parse(txId),
                    fromOutputIndex: (uint)document["Index"].AsInt32,
                    amount: new Money(document["Value"].AsInt64),
                    scriptPubKey: Script.FromHex(document["Script"].AsString)),
                Currency = currency,
                SpentTxPoints = spentHash != null
                    ? new List<BitcoinTxPoint>
                    {
                        new BitcoinTxPoint
                        {
                            Hash = spentHash,
                            Index = (uint)spentIndex
                        }
                    }
                    : null,
                IsConfirmed = isConfirmed,
                IsSpentConfirmed = isSpentConfirmed
            };
        }
    }
}