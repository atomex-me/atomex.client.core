﻿using System.Collections.Generic;
using System.Numerics;

namespace Atomex.Blockchain
{
    public class Token
    {
        public string Id => $"{Contract}:{TokenId}";
        public string Contract { get; set; }
        public string ContractAlias { get; set; }
        public string Standard { get; set; }
        public BigInteger TokenId { get; set; } = 0; // FA1.2 default
        public string Symbol { get; set; }
        public string Name { get; set; }
        public int Decimals { get; set; } = 0; // NFT default
        public string Description { get; set; }
        public string ArtifactUri { get; set; }
        public string DisplayUri { get; set; }
        public string ThumbnailUri { get; set; }
        public List<string> Creators { get; set; }

        public bool HasDescription =>
            !string.IsNullOrEmpty(Description);

        public bool IsNft =>
            !string.IsNullOrEmpty(ArtifactUri);

        public string ContractType => Standard switch
        {
            "fa1.2" => "FA12",
            "fa2" => "FA2",
            "erc20" => "ERC20",
            "erc721" => "ERC721",
            _ => Standard
        };
    }
}