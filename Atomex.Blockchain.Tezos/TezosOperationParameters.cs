using Netezos.Forging.Models;

namespace Atomex.Blockchain.Tezos
{
    public class TezosOperationParameters
    {
        public ManagerOperationContent Content { get; set; }
        public string From => Content.Source;
        public bool UseFeeFromNetwork { get; set; }
        public bool UseGasLimitFromNetwork { get; set; }
        public bool UseStorageLimitFromNetwork { get; set; }
        public bool UseSafeStorageLimit { get; set; }
    }
}