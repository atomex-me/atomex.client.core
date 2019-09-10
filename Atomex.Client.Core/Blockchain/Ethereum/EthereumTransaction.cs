using System;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Core.Entities;
using Atomex.Wallet.Abstract;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Serilog;
using Transaction = Nethereum.RPC.Eth.DTOs.Transaction;

namespace Atomex.Blockchain.Ethereum
{
    public class EthereumTransaction : IAddressBasedTransaction
    {
        public const int UnknownTransaction = 0;
        public const int InputTransaction = 1;
        public const int OutputTransaction = 2;
        public const int SelfTransaction = 3;
        private const int DefaultConfirmations = 1;
        private const string InternalSuffix = "_internal_";

        public string Id { get; set; }
        public Currency Currency { get; set; }
        public BlockInfo BlockInfo { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string Input { get; set; }
        public BigInteger Amount { get; set; }
        public BigInteger Nonce { get; set; }
        public BigInteger GasPrice { get; set; }
        public BigInteger GasLimit { get; set; }
        public BigInteger GasUsed { get; set; }
        public string RlpEncodedTx { get; set; }
        public int Type { get; set; }
        public bool ReceiptStatus { get; set; }
        public bool IsInternal { get; set; }
        public int InternalIndex { get; set; }

        public string UniqueId => Id + (IsInternal ? $"{InternalSuffix}{InternalIndex}" : string.Empty);

        public EthereumTransaction()
        {
        }

        public EthereumTransaction(Currency currency)
        {
            Currency = currency;
            BlockInfo = new BlockInfo
            {
                FirstSeen = DateTime.UtcNow
            };
        }

        public EthereumTransaction(
            Currency currency,
            Transaction tx,
            TransactionReceipt txReceipt,
            DateTime blockTimeStamp)
        {
            Currency = currency;
            Id = tx.TransactionHash;
            From = tx.From.ToLowerInvariant();
            To = tx.To.ToLowerInvariant();
            Input = tx.Input;
            Amount = tx.Value;
            Nonce = tx.Nonce;
            GasPrice = tx.GasPrice;
            GasLimit = tx.Gas;
            GasUsed = txReceipt.GasUsed;
            Type = UnknownTransaction;
            ReceiptStatus = txReceipt.Status != null
                ? txReceipt.Status.Value == BigInteger.One
                : true;
            IsInternal = false;
            InternalIndex = 0;

            BlockInfo = new BlockInfo
            {
                BlockHeight = (long) tx.TransactionIndex.Value,
                Fees = (long) txReceipt.GasUsed.Value,
                Confirmations = (int) txReceipt.Status.Value,
                BlockTime = blockTimeStamp,
                FirstSeen = blockTimeStamp
            };
        }

        public EthereumTransaction(Currency currency, TransactionInput txInput)
        {
            Currency = currency;
            From = txInput.From.ToLowerInvariant();
            To = txInput.To.ToLowerInvariant();
            Input = txInput.Data;
            Amount = txInput.Value;
            Nonce = txInput.Nonce;
            GasPrice = txInput.GasPrice;
            GasLimit = txInput.Gas;

            BlockInfo = new BlockInfo
            {
                FirstSeen = DateTime.UtcNow
            };
        }

        public bool IsConfirmed() => BlockInfo?.Confirmations >= DefaultConfirmations;

        public bool Verify()
        {
            var currency = (Atomex.Ethereum)Currency;

            return Web3.OfflineTransactionSigner
                .VerifyTransaction(
                    rlp: RlpEncodedTx,
                    chain: currency.Chain);
        }

        public byte[] ToBytes()
        {
            return Encoding.UTF8.GetBytes(RlpEncodedTx);
        }

        public async Task<bool> SignAsync(
            IKeyStorage keyStorage,
            WalletAddress address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (address.KeyIndex == null)
            {
                Log.Error(
                    messageTemplate: "Can't find private key for address {@address}",
                    propertyValue: address);
                return false;
            }

            var privateKey = keyStorage.GetPrivateKey(Currency, address.KeyIndex);

            return await SignAsync(privateKey)
                .ConfigureAwait(false);
        }

        private Task<bool> SignAsync(byte[] privateKey)
        {
            if (privateKey == null)
                throw new ArgumentNullException(nameof(privateKey));

            RlpEncodedTx = Web3.OfflineTransactionSigner
                .SignTransaction(
                    privateKey: privateKey,
                    chain: GetCurrency().Chain,
                    to: To,
                    amount: Amount,
                    nonce: Nonce,
                    gasPrice: GasPrice,
                    gasLimit: GasLimit,
                    data: Input);
            
            From = Web3.OfflineTransactionSigner
                .GetSenderAddress(RlpEncodedTx, GetCurrency().Chain)
                .ToLowerInvariant();

            return Task.FromResult(true);
        }

        private Atomex.Ethereum GetCurrency()
        {
            return (Atomex.Ethereum) Currency;
        }
    }
}