using AccessWifi.Api.Features.Companies;

namespace AccessWifi.Api.Features.Admin
{
    public record LoginResponse(string Token, string Role, CompanySummaryDto? Company);
}