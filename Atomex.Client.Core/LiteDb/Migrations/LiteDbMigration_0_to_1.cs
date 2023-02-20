using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

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
    public class LiteDbMigration_0_to_1
    {
        private const string Temp = "temp";
        private const string Backup = "backup";
        private const string TxIdKey = "TxId";
        private const string HexKey = "Hex";
        private const string OrderIdKey = "OrderId";

        public static LiteDbMigrationResult Migrate(string pathToDb, string sessionPassword, Network network)
        {
            var oldConnectionString = $"FileName={pathToDb};Password={sessionPassword};Connection=direct;Upgrade=true";
            var newConnectionString = $"FileName={pathToDb}.{Temp};Password={sessionPassword};Connection=direct";

            var mapper = CreateBsonMapper();

            using (var oldDb = new LiteDatabase(oldConnectionString))
            using (var newDb = new LiteDatabase(newConnectionString, mapper))
            {
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

                    var txNetwork = (currency, network) switch
                    {
                        ("BTC", Network.MainNet) => NBitcoin.Network.Main,
                        ("BTC", Network.TestNet) => NBitcoin.Network.TestNet,
                        ("LTC", Network.MainNet) => NBitcoin.Altcoins.Litecoin.Instance.Mainnet,
                        ("LTC", Network.TestNet) => NBitcoin.Altcoins.Litecoin.Instance.Testnet,
                        _ => throw new Exception($"Invalid currency {currency} or network {network}")
                    };

                    var tx = new BitcoinTransaction(
                        currency: currency,
                        tx: Transaction.Parse(oldTx["Tx"].AsString, txNetwork),
                        creationTime: creationTime,
                        blockTime: blockTime,
                        blockHeight: blockHeight,
                        confirmations: confirmations,
                        fee: fee);

                    var txDocument = mapper.ToDocument(tx);

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

                    var redeemTxId = oldSwap.ContainsKey("RedeemTxId") && !oldSwap["RedeemTxId"].IsNull
                        ? oldSwap["RedeemTxId"]["TxId"].AsString
                        : null;

                    var refundTxId = oldSwap.ContainsKey("RefundTxId") && !oldSwap["RefundTxId"].IsNull
                        ? oldSwap["RefundTxId"]["TxId"].AsString
                        : null;

                    var swap = new Swap
                    {
                        FromAddress            = oldSwap["FromAddress"].AsString,
                        FromOutputs            = fromOutputs,
                        Id                     = oldSwap["_id"].AsInt64,
                        IsInitiative           = oldSwap["IsInitiative"].AsBoolean,
                        LastRedeemTryTimeStamp = DateTime.MinValue,
                        LastRefundTryTimeStamp = DateTime.MinValue,
                        MakerNetworkFee        = oldSwap["MakerNetworkFee"].AsDecimal,
                        OrderId                = oldSwap["OrderId"].AsInt64,
                        PartyAddress           = oldSwap["PartyAddress"].AsString,
                        PartyPaymentTxId       = oldSwap["PartyPaymentTxId"].AsString,
                        PartyRedeemScript      = oldSwap["PartyRedeemScript"].AsString,
                        PartyRefundAddress     = oldSwap["PartyRefundAddress"].AsString,
                        PartyRewardForRedeem   = oldSwap["PartyRewardForRedeem"].AsDecimal,
                        PaymentTxId            = oldSwap["PaymentTxId"].AsString,
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
                        Status                 = Enum.Parse<SwapStatus>(oldSwap["Status"].AsString, ignoreCase: true),
                        Symbol                 = oldSwap["Symbol"].AsString,
                        TimeStamp              = oldSwap["TimeStamp"].AsDateTime,
                        ToAddress              = oldSwap["ToAddress"].AsString
                    };

                    var swapDocument = mapper.ToDocument(swap);

                    _ = newDb
                        .GetCollection("Swaps")
                        .Upsert(swapDocument);
                }

                newDb.UserVersion = LiteDbMigrationManager.Version1;
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

        public static BsonMapper CreateBsonMapper()
        {
            var mapper = new BsonMapper()
                .UseSerializer(new BigIntegerToBsonSerializer())
                .UseSerializer(new JObjectToBsonSerializer())
                .UseSerializer(new CoinToBsonSerializer());

            mapper.Entity<WalletAddress>()
                .Id(w => w.TokenBalance != null
                    ? GetUniqueWalletId(w.Address, w.Currency, w.TokenBalance.Contract, w.TokenBalance.TokenId)
                    : GetUniqueWalletId(w.Address, w.Currency, null, null));

            mapper.Entity<BitcoinTransaction>()
                .Id(t => GetUniqueTxId(t.Id, t.Currency))
                .Field(t => t.Id, TxIdKey)
                .Field(t => t.ToHex(), HexKey);
                //.Ignore(t => t.Inputs)
                //.Ignore(t => t.Outputs);

            mapper.Entity<EthereumTransaction>()
                .Id(t => GetUniqueTxId(t.Id, t.Currency))
                .Field(t => t.Id, TxIdKey);

            mapper.Entity<TezosOperation>()
                .Id(t => GetUniqueTxId(t.Id, t.Currency))
                .Field(t => t.Id, TxIdKey);

            mapper.Entity<TezosTokenTransfer>()
                .Id(t => GetUniqueTxId(t.Id, t.Currency))
                .Field(t => t.Id, TxIdKey);

            mapper.Entity<Erc20Transaction>()
                .Id(t => GetUniqueTxId(t.Id, t.Currency))
                .Field(t => t.Id, TxIdKey);

            mapper.Entity<Erc20Transaction>()
                .Id(t => GetUniqueTxId(t.Id, t.Currency))
                .Field(t => t.Id, TxIdKey);

            mapper.Entity<TransactionMetadata>()
                .Id(t => GetUniqueTxId(t.Id, t.Currency))
                .Field(t => t.Id, TxIdKey);

            mapper.Entity<Order>()
                .Id(o => o.ClientOrderId)
                .Field(o => o.Id, OrderIdKey);

            return mapper;
        }

        public static string GetUniqueTxId(string txId, string currency) => $"{txId}:{currency}";

        public static string GetUniqueWalletId(string address, string currency, string tokenContract = null, BigInteger? tokenId = null) =>
            tokenContract == null && tokenId == null
                ? $"{address}:{currency}"
                : $"{address}:{currency}:{tokenContract}:{tokenId.Value}";

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

            var isConfirmed = tx.ContainsKey("BlockInfo") && !tx["BlockInfo"].IsNull;

            var spentTx = spentHash != null
                ? db.GetCollection("Transactions")
                    .FindById($"{spentHash}:{currency}")
                : null;

            var isSpentConfirmed = spentTx.ContainsKey("BlockInfo") && !spentTx["BlockInfo"].IsNull;

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