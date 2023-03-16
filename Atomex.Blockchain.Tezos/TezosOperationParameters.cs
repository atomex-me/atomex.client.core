using Netezos.Forging.Models;

namespace Atomex.Blockchain.Tezos
{
    public class TezosOperationParameters
    {
        public ManagerOperationContent Content { get; set; }
        public string From { get; set; }
        public Fee Fee { get; set; }
        public GasLimit GasLimit { get; set; }
        public StorageLimit StorageLimit { get; set; }
    }
}