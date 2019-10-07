﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Common;
using Serilog;

namespace Atomex.Swaps.Ethereum.Tasks
{
    public class EthereumSwapRefundControlTask : BlockchainTask
    {
        private Atomex.Ethereum Eth => (Atomex.Ethereum) Currency;

        public override async Task<bool> CheckCompletion()
        {
            try
            {
                Log.Debug("Ethereum: check refund event");

                var api = new EtherScanApi(Eth);

                var eventsResult = await api.GetContractEventsAsync(
                        address: Eth.SwapContractAddress,
                        fromBlock: Eth.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<RefundedEventDTO>(),
                        topic1: "0x" + Swap.SecretHash.ToHexString())
                    .ConfigureAwait(false);

                if (eventsResult.HasError)
                    return false; // todo: error message?

                var events = eventsResult.Value?.ToList();

                if (events == null || !events.Any())
                    return false;

                Log.Debug("Refund event received");

                CompleteHandler?.Invoke(this);
                return true;  
            }
            catch (Exception e)
            {
                Log.Error(e, "Ethereum refund control task error");
            }

            CancelHandler?.Invoke(this);
            return true;
        }
    }
}