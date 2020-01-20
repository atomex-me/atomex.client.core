using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Serilog;
using Transaction = Nethereum.RPC.Eth.DTOs.Transaction;

namespace Atomex.Blockchain.Ethereum
{
    public class EthereumTransaction : IAddressBasedTransaction
    {
        private const int DefaultConfirmations = 1;

        public string Id { get; set; }
        public Currency Currency { get; set; }
        public BlockInfo BlockInfo { get; set; }
        public BlockchainTransactionState State { get; set; }
        public BlockchainTransactionType Type { get; set; }
        public DateTime? CreationTime { get; set; }
        public bool IsConfirmed => BlockInfo?.Confirmations >= DefaultConfirmations;

        public string From { get; set; }
        public string To { get; set; }
        public string Input { get; set; }
        public BigInteger Amount { get; set; }
        public BigInteger Nonce { get; set; }
        public BigInteger GasPrice { get; set; }
        public BigInteger GasLimit { get; set; }
        public BigInteger GasUsed { get; set; }
        public string RlpEncodedTx { get; set; }
        public bool ReceiptStatus { get; set; }
        public bool IsInternal { get; set; }
        public int InternalIndex { get; set; }
        public List<EthereumTransaction> InternalTxs { get; set; }

        public EthereumTransaction()
        {
        }

        public EthereumTransaction(
            Currency currency,
            Transaction tx,
            TransactionReceipt txReceipt,
            DateTime blockTimeStamp)
        {
            Currency = currency;
            Id = tx.TransactionHash;
            Type = BlockchainTransactionType.Unknown;
            State = txReceipt.Status != null && txReceipt.Status.Value == BigInteger.One
                ? BlockchainTransactionState.Confirmed
                : (txReceipt.Status != null
                    ? BlockchainTransactionState.Failed
                    : BlockchainTransactionState.Unconfirmed);
            CreationTime = blockTimeStamp;

            From = tx.From.ToLowerInvariant();
            To = tx.To.ToLowerInvariant();
            Input = tx.Input;
            Amount = tx.Value;
            Nonce = tx.Nonce;
            GasPrice = tx.GasPrice;
            GasLimit = tx.Gas;
            GasUsed = txReceipt.GasUsed;
            ReceiptStatus = State == BlockchainTransactionState.Confirmed;
            IsInternal = false;
            InternalIndex = 0;

            BlockInfo = new BlockInfo
            {
                Confirmations = txReceipt.Status != null
                    ? (int)txReceipt.Status.Value
                    : 0,
                BlockHash = tx.BlockHash,
                BlockHeight = (long) tx.TransactionIndex.Value,
                BlockTime = blockTimeStamp,
                FirstSeen = blockTimeStamp
            };
        }

        public EthereumTransaction(Currency currency, TransactionInput txInput)
        {
            Currency = currency;
            Type = BlockchainTransactionType.Unknown;
            State = BlockchainTransactionState.Unknown;
            CreationTime = DateTime.UtcNow;

            From = txInput.From.ToLowerInvariant();
            To = txInput.To.ToLowerInvariant();
            Input = txInput.Data;
            Amount = txInput.Value;
            Nonce = txInput.Nonce;
            GasPrice = txInput.GasPrice;
            GasLimit = txInput.Gas;
        }

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
            CancellationToken cancellationToken = default)
        {
            if (address.KeyIndex == null)
            {
                Log.Error("Can't find private key for address {@address}", address);
                return false;
            }

            using var privateKey = keyStorage.GetPrivateKey(Currency, address.KeyIndex);

            return await SignAsync(privateKey)
                .ConfigureAwait(false);
        }

        private Task<bool> SignAsync(SecureBytes privateKey)
        {
            if (privateKey == null)
                throw new ArgumentNullException(nameof(privateKey));

            using var scopedPrivateKey = privateKey.ToUnsecuredBytes();

            var chain = ((Atomex.Ethereum) Currency).Chain;

            RlpEncodedTx = Web3.OfflineTransactionSigner
                .SignTransaction(
                    privateKey: scopedPrivateKey,
                    chain: chain,
                    to: To,
                    amount: Amount,
                    nonce: Nonce,
                    gasPrice: GasPrice,
                    gasLimit: GasLimit,
                    data: Input);
            
            From = Web3.OfflineTransactionSigner
                .GetSenderAddress(RlpEncodedTx, chain)
                .ToLowerInvariant();

            return Task.FromResult(true);
        }
    }
}