﻿using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Wallet.Abstract
{
    public interface IHdWalletScanner_OLD
    {
        Task ScanAsync(
            string currency,
            bool skipUsed = false,
            CancellationToken cancellationToken = default);

        Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default);

        Task ScanFreeAddressesAsync(
            CancellationToken cancellationToken = default);

        Task ScanAddressAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default);
    }
}