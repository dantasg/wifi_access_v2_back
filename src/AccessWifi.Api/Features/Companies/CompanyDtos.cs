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
        DateTime? LastReportSentAt)
    {
        public static CompanyDto FromEntity(Company objCompany)
        {
            return new CompanyDto(
                objCompany.Id, objCompany.Name, objCompany.Slug, objCompany.Active,
                objCompany.CreatedAt, objCompany.ReportEmail, objCompany.ReportSendDay,
                objCompany.LastReportSentAt);
        }
    }

    public record CreateCompanyRequest(
        string Name, string Slug, string? ReportEmail, int? ReportSendDay);

    public record UpdateCompanyRequest(
        string Name, bool Active, string? ReportEmail, int? ReportSendDay);
}
