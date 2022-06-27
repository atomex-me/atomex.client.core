namespace Atomex.Client.Rest
{
    public record AuthenticationRequestData(
        string Message,
        long TimeStamp,
        string PublicKey,
        string Signature,
        string Algorithm
    );
}