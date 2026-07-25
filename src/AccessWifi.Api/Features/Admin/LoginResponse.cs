using AccessWifi.Api.Features.Companies;

namespace AccessWifi.Api.Features.Admin
{
    public record LoginResponse(string Token, string RefreshToken, string Role, CompanySummaryDto? Company);

    public record RefreshRequest(string RefreshToken);
}