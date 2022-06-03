using System;
using System.Collections.Generic;
using System.Text;

namespace Atomex.Services;

public partial class RestAtomexClient
{
    private record AuthenticationRequestContent(
        string Message,
        long TimeStamp,
        string PublicKey,
        string Signature,
        string Algorithm
    );

    private record AuthenticationData(string Id, string Token, long Expires);
}
