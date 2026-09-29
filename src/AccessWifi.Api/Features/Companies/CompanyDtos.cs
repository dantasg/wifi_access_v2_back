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
        string? ReportEmail,
        int ReportSendDay,
        DateTime? LastReportSentAt,
        // Fuso das lojas (IANA) e os tipos de campanha liberados para a empresa (D6/D8).
        string TimeZone,
        IReadOnlyList<string> CampaignKinds)
    {
        public static CompanyDto FromEntity(Company objCompany, IReadOnlyList<string> objCampaignKinds)
        {
            return new CompanyDto(
                objCompany.Id, objCompany.Name, objCompany.Slug, objCompany.Active,
                objCompany.CreatedAt, objCompany.ReportEmail, objCompany.ReportSendDay,
                objCompany.LastReportSentAt, objCompany.TimeZone,
                CampaignKind.All.Where(objCampaignKinds.Contains).ToList());
        }
    }

    // TimeZone e CampaignKinds nulos = manter o atual (na criação: Belém e nenhuma campanha).
    public record CreateCompanyRequest(
        string Name, string Slug, string? ReportEmail, int? ReportSendDay,
        string? TimeZone = null, IReadOnlyList<string>? CampaignKinds = null);

    public record UpdateCompanyRequest(
        string Name, bool Active, string? ReportEmail, int? ReportSendDay,
        string? TimeZone = null, IReadOnlyList<string>? CampaignKinds = null);
}
