namespace Atomex.Client.Rest
{
    public record AuthenticationResponseData(
        string Id,
        string Token,
        long Expires
    );
}