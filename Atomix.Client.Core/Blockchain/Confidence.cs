namespace Atomix.Blockchain
{
    public class ConfidenceInformation
    {
        public string TxId { get; set; }
        public decimal Confidence { get; set; }
        public int Confirmations { get; set; }
    }
}