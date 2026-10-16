using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Persistence;

namespace Models.Campaigns
{
    /// <summary>
    /// Demonstração de campanha (PROPOSTA_DEMONSTRACAO_CAMPANHA.md): o PDF de campanha com todos os clientes de
    /// uma unidade e uma mensagem padrão, que o super admin manda para o e-mail da unidade numa apresentação.
    /// Não cria execução: não mexe no limite de uma mensagem por dia, no "já recebeu" nem nos números.
    /// </summary>
    public static class CampaignDemo
    {
        /// <summary>Mensagem padrão (sem artigo antes de {empresa}, como os textos iniciais das campanhas).</summary>
        public const string Message =
            "Oi, {primeiro_nome}! Obrigado por usar o nosso Wi-Fi. Temos novidades esperando por você nesta semana. "
            + "Um abraço da equipe {empresa}.";

        private const int MaxMessageChars = 4000;

        /// <summary>Todos os clientes cadastrados na unidade (quem já se conectou nela).</summary>
        public static IQueryable<Customer> Customers(AppDbContext objDbContext, Guid objUnitId) =>
            objDbContext.Customers.Where(customer =>
                objDbContext.CustomerUnits.Any(link => link.IDCustomer == customer.Id && link.IDUnit == objUnitId));

        /// <summary>
        /// O PDF da unidade: cada cliente com a mensagem já personalizada (o link do WhatsApp vai pronto) e, na
        /// coluna de informação, a última visita dele nesta unidade.
        /// </summary>
        public static async Task<CampaignPdfData> LoadAsync(
            AppDbContext objDbContext, Unit objUnit, DateOnly dtDate, CancellationToken objCancellationToken = default)
        {
            Company objCompany = await objDbContext.Companies.AsNoTracking()
                .FirstAsync(company => company.Id == objUnit.IDCompany, objCancellationToken);
            PortalSettings? objSettings = await objDbContext.PortalSettings.AsNoTracking()
                .FirstOrDefaultAsync(settings => settings.IDCompany == objUnit.IDCompany, objCancellationToken);
            TimeZoneInfo objZone = CompanyTimeZone.Resolve(objCompany.TimeZone);

            var objCustomers = await (
                from customer in objDbContext.Customers.AsNoTracking()
                join link in objDbContext.CustomerUnits.AsNoTracking() on customer.Id equals link.IDCustomer
                where link.IDUnit == objUnit.Id
                select new
                {
                    customer.Name, customer.Phone, customer.Instagram, customer.BirthDate, customer.FirstVisitDate,
                    link.LastVisitAt,
                })
                .ToListAsync(objCancellationToken);

            List<CampaignPdfRow> objRows = objCustomers
                .OrderBy(customer => customer.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(customer =>
                {
                    string sMessage = CampaignMessage.Render(Message, new CampaignMessageData(
                        customer.Name, objCompany.Name, objUnit.Name,
                        CampaignMessage.AgeOn(customer.BirthDate, dtDate),
                        CampaignMessage.FullYearsBetween(customer.FirstVisitDate, dtDate)));
                    string sLastVisit = CompanyTimeZone.Today(objZone, customer.LastVisitAt)
                        .ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                    return new CampaignPdfRow(
                        customer.Name, customer.Phone, InstagramHandle.Normalize(customer.Instagram), sLastVisit, false,
                        sMessage.Length <= MaxMessageChars ? sMessage : sMessage[..MaxMessageChars]);
                })
                .ToList();

            return new CampaignPdfData(
                objCompany.Name, objUnit.Name, CampaignKind.Label(CampaignKind.Demo), CampaignKind.Demo, dtDate, Message,
                objSettings?.Logo, objSettings?.Colors ?? new ThemeColors(), objRows);
        }
    }
}
