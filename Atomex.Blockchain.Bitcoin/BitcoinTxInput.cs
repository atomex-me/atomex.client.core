namespace Atomex.Blockchain.Bitcoin
{
    public class BitcoinTxInput
    {
        public uint Index { get; set; }
        public BitcoinTxPoint PreviousOutput { get; set; }
        public string ScriptSig { get; set; }
    }
}