namespace Atomex.Api.Rest;

public record AuthenticationRequestData(
    string Message,
    long TimeStamp,
    string PublicKey,
    string Signature,
    string Algorithm
);
