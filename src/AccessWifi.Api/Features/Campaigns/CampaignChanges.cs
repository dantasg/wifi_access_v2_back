using System.Text.Json;
using Models.Campaigns;

namespace AccessWifi.Api.Features.Campaigns
{
    /// <summary>O que mudou entre duas versões, em português, para o histórico (ex.: "Horário: 09:00 → 10:00").</summary>
    public static class CampaignChanges
    {
        private static readonly JsonSerializerOptions s_objJson = new(JsonSerializerDefaults.Web);

        public static string Describe(string sOldName, CampaignConfig objOld, string sNewName, CampaignConfig objNew)
        {
            List<string> objParts = [];

            if (sOldName != sNewName)
            {
                objParts.Add($"Nome: \"{sOldName}\" → \"{sNewName}\"");
            }
            if (objOld.Channel != objNew.Channel)
            {
                objParts.Add($"Canal: {objOld.Channel} → {objNew.Channel}");
            }
            if (objOld.SendTime != objNew.SendTime)
            {
                objParts.Add($"Horário: {objOld.SendTime} → {objNew.SendTime}");
            }
            if (objOld.Message != objNew.Message)
            {
                objParts.Add("Mensagem alterada");
            }

            CampaignScheduleConfig? objOldSchedule = objOld.Schedule;
            CampaignScheduleConfig? objNewSchedule = objNew.Schedule;
            if (objOldSchedule?.Recurrence != objNewSchedule?.Recurrence)
            {
                objParts.Add($"Repetição: {RecurrenceText(objOldSchedule?.Recurrence)} → {RecurrenceText(objNewSchedule?.Recurrence)}");
            }
            if (objOldSchedule?.StartDate != objNewSchedule?.StartDate)
            {
                objParts.Add($"Início: {Data(objOldSchedule?.StartDate)} → {Data(objNewSchedule?.StartDate)}");
            }
            if (objOldSchedule?.EndDate != objNewSchedule?.EndDate)
            {
                objParts.Add($"Fim: {Data(objOldSchedule?.EndDate)} → {Data(objNewSchedule?.EndDate)}");
            }
            if (Json(objOldSchedule?.DaysOfWeek) != Json(objNewSchedule?.DaysOfWeek))
            {
                objParts.Add("Dias da semana alterados");
            }
            if (Json(objOld.Filters) != Json(objNew.Filters))
            {
                objParts.Add("Filtros alterados");
            }
            if (objOld.ResendAfterDays != objNew.ResendAfterDays)
            {
                objParts.Add($"Não repetir para quem recebeu nos últimos: {DaysText(objOld.ResendAfterDays)} → {DaysText(objNew.ResendAfterDays)}");
            }
            if (objOld.AbsenceDays != objNew.AbsenceDays)
            {
                objParts.Add($"Dias sem voltar: {DaysText(objOld.AbsenceDays)} → {DaysText(objNew.AbsenceDays)}");
            }
            if (objOld.VisitMilestone != objNew.VisitMilestone)
            {
                objParts.Add($"A cada quantas visitas: {objOld.VisitMilestone?.ToString() ?? "—"} → {objNew.VisitMilestone?.ToString() ?? "—"}");
            }

            return string.Join("; ", objParts);
        }

        private static string Json<T>(T objValue) => JsonSerializer.Serialize(objValue, s_objJson);

        private static string Data(DateOnly? dtValue) => dtValue?.ToString("dd/MM/yyyy") ?? "—";

        private static string DaysText(int? iValue) => iValue is int iDays ? $"{iDays} dias" : "—";

        private static string RecurrenceText(string? sValue) => sValue switch
        {
            CampaignRecurrence.Once => "uma vez",
            CampaignRecurrence.Daily => "diária",
            CampaignRecurrence.Weekly => "semanal",
            CampaignRecurrence.Monthly => "mensal",
            CampaignRecurrence.Yearly => "anual",
            _ => "—",
        };
    }
}
