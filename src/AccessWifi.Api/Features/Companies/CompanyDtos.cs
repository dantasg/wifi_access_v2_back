using Models.DataBase;

namespace AccessWifi.Api.Features.Companies
{
    public record CompanySummaryDto(Guid Id, string Name, string Slug)
    {
        public static CompanySummaryDto FromEntity(Company objCompany)
        {
            return new CompanySummaryDto(objCompany.Id, objCompany.Name, objCompany.Slug);
        }
    }

    public record CompanyDto(
        Guid Id,
        string Name,
        string Slug,
        bool Active,
        DateTime CreatedAt,
        // Dia do relatório mensal; o e-mail é de cada unidade (D24).
        int ReportSendDay,
        // Fuso das lojas (IANA) e os tipos de campanha liberados para a empresa (D6/D8).
        string TimeZone,
        IReadOnlyList<string> CampaignKinds)
    {
        public static CompanyDto FromEntity(Company objCompany, IReadOnlyList<string> objCampaignKinds)
        {
            return new CompanyDto(
                objCompany.Id, objCompany.Name, objCompany.Slug, objCompany.Active,
                objCompany.CreatedAt, objCompany.ReportSendDay, objCompany.TimeZone,
                CampaignKind.All
                    .Where(sKind => CampaignKind.IsAvailable(sKind) && objCampaignKinds.Contains(sKind))
                    .ToList());
        }
    }

    // TimeZone e CampaignKinds nulos = manter o atual (na criação: Belém e nenhuma campanha).
    public record CreateCompanyRequest(
        string Name, string Slug, int? ReportSendDay,
        string? TimeZone = null, IReadOnlyList<string>? CampaignKinds = null);

    public record UpdateCompanyRequest(
        string Name, bool Active, int? ReportSendDay,
        string? TimeZone = null, IReadOnlyList<string>? CampaignKinds = null);
}
