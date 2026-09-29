using Models.DataBase;
using Models.Persistence;

namespace Models.Campaigns
{
    /// <summary>
    /// Quem recebe um disparo. Os filtros são avaliados na hora de cada execução (D16): quem se
    /// cadastrou hoje já entra na campanha de amanhã. A mesma consulta alimenta a prévia de alcance
    /// na tela e a seleção da execução.
    /// </summary>
    public static class CampaignAudience
    {
        private static readonly string[] s_arrDelivered = [.. CampaignRecipientStatus.Delivered];

        /// <param name="objCampaignId">
        /// Campanha já existente (para não repetir quem já recebeu); null na prévia de uma nova.
        /// </param>
        /// <param name="dtLocalDate">Dia do disparo no fuso da empresa.</param>
        /// <param name="dtOccurrenceUtc">Instante do disparo.</param>
        public static IQueryable<Customer> Query(
            AppDbContext objDbContext,
            Guid objCompanyId,
            Guid? objCampaignId,
            string sKind,
            CampaignConfig objConfig,
            DateOnly dtLocalDate,
            DateTime dtOccurrenceUtc)
        {
            IQueryable<Customer> objQuery = objDbContext.Customers
                .Where(customer => customer.IDCompany == objCompanyId);

            // Quem é de 29/02 comemora em 28/02 nos anos não bissextos (D3).
            int iMonth = dtLocalDate.Month;
            int iDay = dtLocalDate.Day;
            bool bInclui29DeFevereiro = iMonth == 2 && iDay == 28 && !DateTime.IsLeapYear(dtLocalDate.Year);

            // Execuções desta campanha: base para "não repetir quem já recebeu".
            IQueryable<Guid> objRunsDaCampanha = objDbContext.CampaignRuns
                .Where(run => run.IDCampaign == objCampaignId)
                .Select(run => run.Id);

            switch (sKind)
            {
                case CampaignKind.Birthday:
                    objQuery = objQuery.Where(customer =>
                        customer.BirthDate != null
                        && customer.BirthDate.Value.Month == iMonth
                        && (customer.BirthDate.Value.Day == iDay
                            || (bInclui29DeFevereiro && customer.BirthDate.Value.Day == 29)));
                    break;

                case CampaignKind.SignupAnniversary:
                    int iYear = dtLocalDate.Year;
                    objQuery = objQuery.Where(customer =>
                        customer.FirstVisitDate.Year < iYear
                        && customer.FirstVisitDate.Month == iMonth
                        && (customer.FirstVisitDate.Day == iDay
                            || (bInclui29DeFevereiro && customer.FirstVisitDate.Day == 29)));
                    break;

                case CampaignKind.Welcome:
                    DateOnly dtOntem = dtLocalDate.AddDays(-1);
                    objQuery = objQuery.Where(customer => customer.FirstVisitDate == dtOntem);
                    break;

                case CampaignKind.WeMissYou:
                    DateTime dtLimite = dtOccurrenceUtc.AddDays(-(objConfig.AbsenceDays ?? 30));
                    objQuery = objQuery.Where(customer => customer.LastVisitAt <= dtLimite);
                    if (objCampaignId is not null)
                    {
                        // Uma vez por ausência: quem já recebeu depois da última visita não recebe de
                        // novo; se voltar e sumir outra vez, recebe.
                        objQuery = objQuery.Where(customer => !objDbContext.CampaignRecipients.Any(recipient =>
                            recipient.IDCustomer == customer.Id
                            && recipient.CreatedAt >= customer.LastVisitAt
                            && s_arrDelivered.Contains(recipient.Status)
                            && objRunsDaCampanha.Contains(recipient.IDRun)));
                    }
                    break;

                case CampaignKind.FrequentCustomer:
                    int iStep = objConfig.VisitMilestone ?? 5;
                    objQuery = objQuery.Where(customer => customer.VisitCount >= iStep);
                    if (objCampaignId is not null)
                    {
                        // Cada marco (5ª, 10ª…) uma vez só.
                        objQuery = objQuery.Where(customer => !objDbContext.CampaignRecipients.Any(recipient =>
                            recipient.IDCustomer == customer.Id
                            && recipient.Milestone == customer.VisitCount / iStep * iStep
                            && s_arrDelivered.Contains(recipient.Status)
                            && objRunsDaCampanha.Contains(recipient.IDRun)));
                    }
                    break;

                case CampaignKind.Filtered:
                    objQuery = ApplyFilters(objDbContext, objQuery, objConfig.Filters, dtLocalDate);
                    if (objCampaignId is not null && objConfig.ResendAfterDays is int iDias)
                    {
                        DateTime dtDesde = dtOccurrenceUtc.AddDays(-iDias);
                        objQuery = objQuery.Where(customer => !objDbContext.CampaignRecipients.Any(recipient =>
                            recipient.IDCustomer == customer.Id
                            && recipient.CreatedAt >= dtDesde
                            && s_arrDelivered.Contains(recipient.Status)
                            && objRunsDaCampanha.Contains(recipient.IDRun)));
                    }
                    break;

                default:
                    return objQuery.Where(_ => false);
            }

            return objQuery;
        }

        private static IQueryable<Customer> ApplyFilters(
            AppDbContext objDbContext, IQueryable<Customer> objQuery, CampaignFilters? objFilters, DateOnly dtLocalDate)
        {
            if (objFilters is null)
            {
                return objQuery;
            }

            if (objFilters.UnitIds is { Count: > 0 })
            {
                Guid[] arrUnits = [.. objFilters.UnitIds];
                objQuery = objQuery.Where(customer => objDbContext.CustomerUnits.Any(link =>
                    link.IDCustomer == customer.Id && arrUnits.Contains(link.IDUnit)));
            }
            if (objFilters.AgeMin is int iAgeMin)
            {
                // Tem pelo menos iAgeMin anos: nasceu até esta data.
                DateOnly dtNascidoAte = dtLocalDate.AddYears(-iAgeMin);
                objQuery = objQuery.Where(customer => customer.BirthDate != null && customer.BirthDate <= dtNascidoAte);
            }
            if (objFilters.AgeMax is int iAgeMax)
            {
                // Tem no máximo iAgeMax anos: ainda não fez iAgeMax + 1.
                DateOnly dtNascidoDepoisDe = dtLocalDate.AddYears(-(iAgeMax + 1));
                objQuery = objQuery.Where(customer => customer.BirthDate != null && customer.BirthDate > dtNascidoDepoisDe);
            }
            if (objFilters.BirthMonths is { Count: > 0 })
            {
                int[] arrMonths = [.. objFilters.BirthMonths];
                objQuery = objQuery.Where(customer =>
                    customer.BirthDate != null && arrMonths.Contains(customer.BirthDate.Value.Month));
            }
            if (objFilters.FirstVisitFrom is DateOnly dtFrom)
            {
                objQuery = objQuery.Where(customer => customer.FirstVisitDate >= dtFrom);
            }
            if (objFilters.FirstVisitTo is DateOnly dtTo)
            {
                objQuery = objQuery.Where(customer => customer.FirstVisitDate <= dtTo);
            }
            if (objFilters.LastVisitWithinDays is int iWithin)
            {
                DateOnly dtDesde = dtLocalDate.AddDays(-iWithin);
                objQuery = objQuery.Where(customer => customer.LastVisitDate >= dtDesde);
            }
            if (objFilters.LastVisitOlderThanDays is int iOlder)
            {
                DateOnly dtAntesDe = dtLocalDate.AddDays(-iOlder);
                objQuery = objQuery.Where(customer => customer.LastVisitDate < dtAntesDe);
            }
            if (objFilters.VisitsMin is int iVisits)
            {
                objQuery = objQuery.Where(customer => customer.VisitCount >= iVisits);
            }
            if (objFilters.HasInstagram is bool bHasInstagram)
            {
                objQuery = bHasInstagram
                    ? objQuery.Where(customer => customer.Instagram != "")
                    : objQuery.Where(customer => customer.Instagram == "");
            }

            return objQuery;
        }
    }
}
