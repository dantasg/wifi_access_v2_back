using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Persistence;

namespace Models.Campaigns
{
    /// <summary>
    /// Monta o PDF de uma unidade numa execução a partir do que está gravado (D20). O mesmo código serve ao
    /// serviço, que manda o e-mail, e ao painel, que baixa o PDF de novo pelo histórico.
    /// </summary>
    public static class CampaignDeliveryDocument
    {
        /// <param name="arrStatuses">Quais destinatários entram (o serviço manda os pendentes; o histórico, os enviados).</param>
        public static async Task<CampaignPdfData> LoadAsync(
            AppDbContext objDbContext,
            CampaignRun objRun,
            Guid? objUnitId,
            IReadOnlyCollection<string> arrStatuses,
            CancellationToken objCancellationToken = default)
        {
            Campaign objCampaign = await objDbContext.Campaigns.AsNoTracking()
                .FirstAsync(campaign => campaign.Id == objRun.IDCampaign, objCancellationToken);
            // D13: a mensagem é a da versão com que a execução rodou.
            string sConfigJson = await objDbContext.CampaignVersions.AsNoTracking()
                .Where(version => version.Id == objRun.IDCampaignVersion)
                .Select(version => version.ConfigJson)
                .FirstAsync(objCancellationToken);
            string sCompanyName = await objDbContext.Companies.AsNoTracking()
                .Where(company => company.Id == objRun.IDCompany)
                .Select(company => company.Name)
                .FirstAsync(objCancellationToken);
            PortalSettings? objSettings = await objDbContext.PortalSettings.AsNoTracking()
                .FirstOrDefaultAsync(settings => settings.IDCompany == objRun.IDCompany, objCancellationToken);
            string sUnitName = objUnitId is Guid objId
                ? await objDbContext.Units.AsNoTracking()
                    .Where(unit => unit.Id == objId).Select(unit => unit.Name)
                    .FirstOrDefaultAsync(objCancellationToken) ?? ""
                : "";

            List<CampaignRecipient> objRecipients = await objDbContext.CampaignRecipients.AsNoTracking()
                .Where(recipient => recipient.IDRun == objRun.Id && recipient.IDUnit == objUnitId
                    && arrStatuses.Contains(recipient.Status))
                .ToListAsync(objCancellationToken);

            List<CampaignPdfRow> objRows = objRecipients
                // Aniversário: na ordem dos dias da semana; os demais, por nome.
                .OrderBy(recipient => recipient.EventDate ?? DateOnly.MinValue)
                .ThenBy(recipient => recipient.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(recipient => new CampaignPdfRow(
                    recipient.Name, recipient.Phone, recipient.Instagram, recipient.Info,
                    recipient.EventDate == objRun.LocalDate, recipient.Message))
                .ToList();

            return new CampaignPdfData(
                sCompanyName, sUnitName, objCampaign.Name, objCampaign.Kind, objRun.LocalDate,
                CampaignConfig.FromJson(sConfigJson).Message, objSettings?.Logo,
                objSettings?.Colors ?? new ThemeColors(), objRows);
        }

        /// <summary>"campanha-aniversario-itaituba-2026-10-12.pdf".</summary>
        public static string FileName(string sKind, string sUnitSlug, DateOnly dtDate) =>
            $"campanha-{CampaignKind.FileSlug(sKind)}-{(sUnitSlug.Length > 0 ? sUnitSlug : "sem-unidade")}-{dtDate:yyyy-MM-dd}.pdf";
    }
}
