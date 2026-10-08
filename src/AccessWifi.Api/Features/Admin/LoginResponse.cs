using AccessWifi.Api.Features.Companies;

namespace AccessWifi.Api.Features.Admin
{
    /// <summary>Units: null = sem restrição; com itens = usuário de unidade (só vê essas).</summary>
    public record LoginResponse(
        string Token, string RefreshToken, string Role, CompanySummaryDto? Company,
        IReadOnlyList<UserUnitDto>? Units = null);

    public record RefreshRequest(string RefreshToken);
}
