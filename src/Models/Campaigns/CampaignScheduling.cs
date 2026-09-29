using Models.DataBase;

namespace Models.Campaigns
{
    public static class CampaignScheduling
    {
        /// <summary>
        /// Recalcula o próximo disparo. Pausada, ou com o tipo desligado para a empresa (D6): sem
        /// próximo disparo. Ativa sem nenhuma data futura na agenda: encerrada.
        /// </summary>
        public static void RefreshNextRun(Campaign objCampaign, TimeZoneInfo objZone, DateTime dtNowUtc, bool bKindEnabled)
        {
            if (objCampaign.Status != CampaignStatus.Active || !bKindEnabled)
            {
                objCampaign.NextRunAt = null;
                return;
            }

            objCampaign.NextRunAt = CampaignCalendar.NextOccurrence(
                objCampaign.Kind, CampaignConfig.FromJson(objCampaign.ConfigJson), objZone, dtNowUtc);
            if (objCampaign.NextRunAt is null)
            {
                objCampaign.Status = CampaignStatus.Finished;
            }
        }
    }
}
