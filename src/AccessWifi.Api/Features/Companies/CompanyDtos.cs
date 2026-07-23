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

    public record CompanyUnifiDto(string Host, string Site, string Username, bool UnifiOs, bool VerifySsl)
    {
        public static CompanyUnifiDto FromEntity(CompanyUnifi objUnifi)
        {
            return new CompanyUnifiDto(
                objUnifi.Host, objUnifi.Site, objUnifi.Username, objUnifi.UnifiOs, objUnifi.VerifySsl);
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
        CompanyUnifiDto Unifi)
    {
        public static CompanyDto FromEntity(Company objCompany)
        {
            return new CompanyDto(
                objCompany.Id, objCompany.Name, objCompany.Slug, objCompany.Active,
                objCompany.CreatedAt, objCompany.ReportEmail, objCompany.ReportSendDay,
                objCompany.LastReportSentAt, CompanyUnifiDto.FromEntity(objCompany.Unifi));
        }
    }

    public record CompanyUnifiRequest(string Host, string Site, string Username, string? Password, bool UnifiOs, bool VerifySsl);

    public record CreateCompanyRequest(
        string Name, string Slug, string? ReportEmail, int? ReportSendDay, CompanyUnifiRequest? Unifi);

    public record UpdateCompanyRequest(
        string Name, bool Active, string? ReportEmail, int? ReportSendDay, CompanyUnifiRequest? Unifi);
}
