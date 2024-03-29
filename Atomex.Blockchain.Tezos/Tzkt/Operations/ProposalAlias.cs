﻿using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt.Operations
{
    public class ProposalAlias
    {
        [JsonPropertyName("alias")]
        public string Alias { get; set; }
        [JsonPropertyName("hash")]
        public string Hash { get; set; }
    }
}