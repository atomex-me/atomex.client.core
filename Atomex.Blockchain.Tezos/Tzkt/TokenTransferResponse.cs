using System;
using System.Text.Json.Serialization;

using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain.Tezos.Tzkt
{
    public class TokenTransferResponse
    {
        /// <summary>
        /// Internal TzKT id.  
        /// **[sortable]**
        /// </summary>
        [JsonPropertyName("id")]
        public long Id { get; set; }

        /// <summary>
        /// Level of the block, at which the token transfer was made.  
        /// **[sortable]**
        /// </summary>
        [JsonPropertyName("level")]
        public int Level { get; set; }

        /// <summary>
        /// Timestamp of the block, at which the token transfer was made.
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Token info.  
        /// Click on the field to expand more details.
        /// </summary>
        [JsonPropertyName("token")]
        public TokenInfo Token { get; set; }

        /// <summary>
        /// Sender account.  
        /// Click on the field to expand more details.
        /// </summary>
        [JsonPropertyName("from")]
        public AccountAddress From { get; set; }

        /// <summary>
        /// Target account.  
        /// Click on the field to expand more details.
        /// </summary>
        [JsonPropertyName("to")]
        public AccountAddress To { get; set; }

        /// <summary>
        /// Amount of tokens transferred (raw value, not divided by `decimals`).  
        /// **[sortable]**
        /// </summary>
        [JsonPropertyName("amount")]
        public string Amount { get; set; }

        /// <summary>
        /// Internal TzKT id of the transaction operation, caused the token transfer.
        /// </summary>
        [JsonPropertyName("transactionId")]
        public long? TransactionId { get; set; }

        /// <summary>
        /// Internal TzKT id of the origination operation, caused the token transfer.
        /// </summary>
        [JsonPropertyName("originationId")]
        public long? OriginationId { get; set; }

        /// <summary>
        /// Internal TzKT id of the migration operation, caused the token transfer.
        /// </summary>
        [JsonPropertyName("migrationId")]
        public long? MigrationId { get; set; }

        public TezosTokenTransfer ToTokenTransfer(string operationHash, int counter, int? nonce)
        {
            var token = Token.ToToken();
            var nonceStr = nonce != null ? nonce.ToString() : string.Empty;

            return new TezosTokenTransfer()
            {
                Id            = $"{operationHash}/{counter}/{nonceStr}",
                Currency      = token.ContractType, // token.Symbol,
                CreationTime  = Timestamp, 
                BlockTime     = Timestamp,
                BlockHeight   = Level,
                Confirmations = 1, // todo: HEAD - LEVEL
                Status        = TransactionStatus.Confirmed,

                From          = From?.Address,
                To            = To?.Address,
                Amount        = Amount,
                Token         = token,
                FromAlias     = From?.Alias,
                ToAlias       = To?.Alias,
                ContractAlias = Token.Contract?.Alias
            };
        }
    }
}