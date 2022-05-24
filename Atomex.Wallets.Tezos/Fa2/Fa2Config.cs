using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Atomex.Wallets.Tezos.Fa2
{
    public class Fa2Config : TezosConfig
    {
        public string TokenContract { get; set; }
        public int TokenId { get; set; }
    }
}
