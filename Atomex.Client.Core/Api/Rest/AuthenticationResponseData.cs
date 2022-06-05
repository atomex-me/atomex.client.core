namespace Atomex.Api.Rest
{
    public record AuthenticationResponseData(
        string Id,
        string Token,
        long Expires
    );
}