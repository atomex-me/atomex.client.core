using System;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Serilog;
using Transaction = Nethereum.RPC.Eth.DTOs.Transaction;

namespace Atomix.Blockchain.Ethereum
{
    public class EthereumTransaction : IAddressBasedTransaction
    {
        public const int UnknownTransaction = 0;
        public const int InputTransaction = 1;
        public const int OutputTransaction = 2;
        public const int SelfTransaction = 3;
        public const int DefaultConfirmations = 1;

        public string Id { get; set; }
        public Currency Currency { get; } = Currencies.Eth;
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

        public EthereumTransaction()
        {
            BlockInfo = new BlockInfo()
            {
                FirstSeen = DateTime.UtcNow
            };
        }

        public EthereumTransaction(Transaction tx, TransactionReceipt txReceipt, DateTime blockTimeStamp)
        {
            Id = tx.TransactionHash;
            From = tx.From.ToLowerInvariant();
            To = tx.To.ToLowerInvariant();
            Input = tx.Input;
            Amount = tx.Value;
            Nonce = tx.Nonce;
            GasPrice = tx.GasPrice;
            GasLimit = tx.Gas;

            BlockInfo = new BlockInfo
            {
                BlockHeight = (long) tx.TransactionIndex.Value,
                Fees = (long) txReceipt.GasUsed.Value,
                Confirmations = (int) txReceipt.Status.Value,
                BlockTime = blockTimeStamp,
                FirstSeen = blockTimeStamp
            };
        }

        public EthereumTransaction(TransactionInput txInput)
        {
            From = txInput.From;
            To = txInput.To;
            Input = txInput.Data;
            Amount = txInput.Value;
            Nonce = txInput.Nonce;
            GasPrice = txInput.GasPrice;
            GasLimit = txInput.Gas;

            BlockInfo = new BlockInfo()
            {
                FirstSeen = DateTime.UtcNow
            };
        }

        public bool IsConfirmed() => BlockInfo?.Confirmations >= DefaultConfirmations;

        public bool Verify()
        {
            var currency = (Atomix.Ethereum)Currency;

            return Web3.OfflineTransactionSigner
                .VerifyTransaction(
                    rlp: RlpEncodedTx,
                    chain: currency.Chain);
        }

        public byte[] ToBytes()
        {
            return Encoding.UTF8.GetBytes(RlpEncodedTx);
        }

        public decimal AmountInEth()
        {
            var gas = GasUsed != 0 ? GasUsed : GasLimit;

            switch (Type)
            {
                case InputTransaction:
                    return Atomix.Ethereum.WeiToEth(Amount);
                case OutputTransaction:
                    return -Atomix.Ethereum.WeiToEth(Amount + GasPrice * gas);
                case SelfTransaction:
                    return -Atomix.Ethereum.WeiToEth(GasPrice * gas);
                default:
                    return Atomix.Ethereum.WeiToEth(Amount + GasPrice * gas);
            }  
        }

        public async Task<bool> SignAsync(
            IPrivateKeyStorage keyStorage,
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var keyIndex = await keyStorage
                .RecoverKeyIndexAsync(Currency, address, cancellationToken)
                .ConfigureAwait(false);

            if (keyIndex == null)
            {
                Log.Error($"Can't find private key for address {address}");
                return false;
            }

            var privateKey = keyStorage.GetPrivateKey(Currency, keyIndex);

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

            //var tx = TransactionFactory.CreateTransaction(RlpEncodedTx);

            From = Web3.OfflineTransactionSigner
                .GetSenderAddress(RlpEncodedTx, GetCurrency().Chain)
                .ToLowerInvariant();

            return Task.FromResult(true);
        }

        private Atomix.Ethereum GetCurrency()
        {
            return (Atomix.Ethereum) Currency;
        }
    }
}