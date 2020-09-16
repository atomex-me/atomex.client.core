﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Core;

namespace Atomex.Blockchain
{
    public class BlockchainTxOutputSource : ITxOutputSource
    {
        private readonly BitcoinBasedCurrency _currency;

        public BlockchainTxOutputSource(BitcoinBasedCurrency currency)
        {
            _currency = currency ?? throw new ArgumentNullException(nameof(currency));
        }

        public async Task<IEnumerable<BitcoinBasedTxOutput>> GetAvailableOutputsAsync(
            IEnumerable<WalletAddress> addresses)
        {
            var outputs = new List<BitcoinBasedTxOutput>();

            foreach (var a in addresses)
            {
                var outputsResult = await ((IInOutBlockchainApi)_currency.BlockchainApi)
                    .GetUnspentOutputsAsync(a.Address)
                    .ConfigureAwait(false);

                if (outputsResult.HasError)
                    break; // todo: return error

                if (outputsResult.Value != null)
                    outputs.AddRange(outputsResult.Value.Cast<BitcoinBasedTxOutput>());
            }

            return outputs;
        }

        public async Task<IEnumerable<BitcoinBasedTxOutput>> GetAvailableOutputsAsync(
            IEnumerable<string> addresses)
        {
            var outputs = new List<BitcoinBasedTxOutput>();

            foreach (var address in addresses)
            {
                var outputsResult = await ((IInOutBlockchainApi)_currency.BlockchainApi)
                    .GetUnspentOutputsAsync(address)
                    .ConfigureAwait(false);

                if (outputsResult.HasError)
                    break; // todo: return error

                if (outputsResult.Value != null)
                    outputs.AddRange(outputsResult.Value.Cast<BitcoinBasedTxOutput>());
            }

            return outputs;
        }
    }
}