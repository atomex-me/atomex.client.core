using Atomex.Core;
using System.Collections.Generic;

namespace Atomex.Services;

#nullable enable

public partial class RestAtomexClient
{
    private record NewOrderDto(
        string ClientOrderId,
        string Symbol,
        decimal Price,
        decimal Qty,
        Side Side,
        OrderType Type,
        List<ProofOfFundsDto>? ProofsOfFunds,
        RequisitesDto Requisites
    );

    private record ProofOfFundsDto(
        string Address,
        string Currency,
        long TimeStamp,
        string Message,
        string PublicKey,
        string Signature,
        string Algorithm
    );

    private record RequisitesDto(
        string? BaseCurrencyContract,
        string? QuoteCurrencyContract
    );

    private record NewOrderResponseDto(long OrderId);

    private record OrderCancelationDto(bool Result);
}
