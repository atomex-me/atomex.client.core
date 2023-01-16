using Netezos.Forging.Models;

using Atomex.Wallet.Tezos;

namespace Atomex.Wallets.Tezos
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