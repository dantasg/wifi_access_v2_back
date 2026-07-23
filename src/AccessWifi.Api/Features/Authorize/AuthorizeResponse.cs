namespace AccessWifi.Api.Features.Authorize
{
    public record AuthorizeResponse(bool Authorized, string? Redirect = null, string? Error = null);
}
