using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt
{
    public class TokenContractResponse
    {
        /// <summary>
        /// Type of the account, `contract` - smart contract programmable account
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// Public key hash of the contract
        /// </summary>
        [JsonPropertyName("address")]
        public string Address { get; set; }

        /// <summary>
        /// Kind of the contract (`delegator_contract` or `smart_contract`),
        /// where `delegator_contract` - manager.tz smart contract for delegation purpose only
        /// </summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        /// <summary>
        /// List of implemented standards (TZIPs)
        /// </summary>
        [JsonPropertyName("tzips")]
        public IEnumerable<string> Tzips { get; set; }
        
        /// <summary>
        /// Name of the project behind the contract or contract description
        /// </summary>
        [JsonPropertyName("alias")]
        public string Alias { get; set; }
        
        /// <summary>
        /// Contract balance (micro tez)
        /// </summary>
        [JsonPropertyName("balance")]
        public long Balance { get; set; }
        
        /// <summary>
        /// Information about the account, which has deployed the contract to the blockchain
        /// </summary>    
        [JsonPropertyName("creator")]   
        public CreatorInfo Creator { get; set; }
        
        /// <summary>
        /// Information about the account, which was marked as a manager when contract was deployed to the blockchain
        /// </summary>
        [JsonPropertyName("manager")]
        public ManagerInfo Manager { get; set; }
        
        /// <summary>
        /// Information about the current delegate of the contract. `null` if it's not delegated
        /// </summary>
        [JsonPropertyName("delegate")]
        public DelegateInfo Delegate { get; set; }

        /// <summary>
        /// Block height of latest delegation. `null` if it's not delegated
        /// </summary>
        [JsonPropertyName("delegationLevel")]
        public int? DelegationLevel { get; set; }

        /// <summary>
        /// Block datetime of latest delegation (ISO 8601, e.g. `2020-02-20T02:40:57Z`). `null` if it's not delegated
        /// </summary>
        [JsonPropertyName("delegationTime")]
        public DateTime? DelegationTime { get; set; }

        /// <summary>
        /// Number of contracts, created (originated) and/or managed by the contract
        /// </summary>
        [JsonPropertyName("numContracts")]
        public int NumContracts { get; set; }

        /// <summary>
        /// Number of account tokens with non-zero balances
        /// </summary>
        [JsonPropertyName("activeTokensCount")]
        public int ActiveTokensCount { get; set; }

        /// <summary>
        /// Number of tokens the account ever had
        /// </summary>
        [JsonPropertyName("tokenBalancesCount")]
        public int TokenBalancesCount { get; set; }

        /// <summary>
        /// Number of token transfers from/to the account
        /// </summary>
        [JsonPropertyName("tokenTransfersCount")]
        public int TokenTransfersCount { get; set; }

        /// <summary>
        /// Number of delegation operations of the contract
        /// </summary>
        [JsonPropertyName("numDelegations")]
        public int NumDelegations { get; set; }

        /// <summary>
        /// Number of origination (deployment / contract creation) operations, related the contract
        /// </summary>
        [JsonPropertyName("numOriginations")]
        public int NumOriginations { get; set; }

        /// <summary>
        /// Number of transaction (transfer) operations, related to the contract
        /// </summary>
        [JsonPropertyName("numTransactions")]
        public int NumTransactions { get; set; }
    
        /// <summary>
        /// Number of reveal (is used to reveal the public key associated with an account) operations of the contract
        /// </summary>
        [JsonPropertyName("numReveals")]
        public int NumReveals { get; set; }

        /// <summary>
        /// Number of migration (result of the context (database) migration during a protocol update) operations
        /// related to the contract (synthetic type)
        /// </summary>
        [JsonPropertyName("numMigrations")]
        public int NumMigrations { get; set; }

        /// <summary>
        /// Block height of the contract creation
        /// </summary>
        [JsonPropertyName("firstActivity")]
        public int FirstActivity { get; set; }

        /// <summary>
        /// Block datetime of the contract creation (ISO 8601, e.g. `2020-02-20T02:40:57Z`)
        /// </summary>
        [JsonPropertyName("firstActivityTime")]
        public DateTime FirstActivityTime { get; set; }

        /// <summary>
        /// Height of the block in which the account state was changed last time
        /// </summary>
        [JsonPropertyName("lastActivity")]
        public int LastActivity { get; set; }

        /// <summary>
        /// Datetime of the block in which the account state was changed last time (ISO 8601, e.g. `2020-02-20T02:40:57Z`)
        /// </summary>
        [JsonPropertyName("lastActivityTime")]
        public DateTime LastActivityTime { get; set; }

        /// <summary>
        /// Contract storage value. Omitted by default. Use `?includeStorage=true` to include it in response.
        /// </summary>
        [JsonPropertyName("storage")]
        public object Storage { get; set; }

        /// <summary>
        /// 32-bit hash of the contract parameter and storage types.
        /// This field can be used for searching similar contracts (which have the same interface).
        /// </summary>
        [JsonPropertyName("typeHash")]
        public int TypeHash { get; set; }

        /// <summary>
        /// 32-bit hash of the contract code.
        /// This field can be used for searching same contracts (which have the same script).
        /// </summary>
        [JsonPropertyName("codeHash")]
        public int CodeHash { get; set; }

        /// <summary>
        /// Metadata of the contract (alias, logo, website, contacts, etc)
        /// </summary>
        [JsonPropertyName("metadata")]
        public ProfileMetadata Metadata { get; set; }

        public TokenContract ToTokenContract()
        {
            var contractType = TezosHelper.Fa2;

            if (Tzips != null)
            {
                if (Tzips.Contains("fa2"))
                    contractType = TezosHelper.Fa2;

                if (Tzips.Contains("fa12"))
                    contractType = TezosHelper.Fa12;
            }

            return new TokenContract()
            {
                Address = Address,
                Name = Alias,
                Type = contractType,
            };
        }
    }
}