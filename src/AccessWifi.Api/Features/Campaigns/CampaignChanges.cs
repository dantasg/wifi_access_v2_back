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
            List<string> objPartes = [];

            if (sOldName != sNewName)
            {
                objPartes.Add($"Nome: \"{sOldName}\" → \"{sNewName}\"");
            }
            if (objOld.Channel != objNew.Channel)
            {
                objPartes.Add($"Canal: {objOld.Channel} → {objNew.Channel}");
            }
            if (objOld.SendTime != objNew.SendTime)
            {
                objPartes.Add($"Horário: {objOld.SendTime} → {objNew.SendTime}");
            }
            if (objOld.Message != objNew.Message)
            {
                objPartes.Add("Mensagem alterada");
            }

            CampaignScheduleConfig? objOldSchedule = objOld.Schedule;
            CampaignScheduleConfig? objNewSchedule = objNew.Schedule;
            if (objOldSchedule?.Recurrence != objNewSchedule?.Recurrence)
            {
                objPartes.Add($"Repetição: {Recorrencia(objOldSchedule?.Recurrence)} → {Recorrencia(objNewSchedule?.Recurrence)}");
            }
            if (objOldSchedule?.StartDate != objNewSchedule?.StartDate)
            {
                objPartes.Add($"Início: {Data(objOldSchedule?.StartDate)} → {Data(objNewSchedule?.StartDate)}");
            }
            if (objOldSchedule?.EndDate != objNewSchedule?.EndDate)
            {
                objPartes.Add($"Fim: {Data(objOldSchedule?.EndDate)} → {Data(objNewSchedule?.EndDate)}");
            }
            if (Json(objOldSchedule?.DaysOfWeek) != Json(objNewSchedule?.DaysOfWeek))
            {
                objPartes.Add("Dias da semana alterados");
            }
            if (Json(objOld.Filters) != Json(objNew.Filters))
            {
                objPartes.Add("Filtros alterados");
            }
            if (objOld.ResendAfterDays != objNew.ResendAfterDays)
            {
                objPartes.Add($"Não repetir para quem recebeu nos últimos: {Dias(objOld.ResendAfterDays)} → {Dias(objNew.ResendAfterDays)}");
            }
            if (objOld.AbsenceDays != objNew.AbsenceDays)
            {
                objPartes.Add($"Dias sem voltar: {Dias(objOld.AbsenceDays)} → {Dias(objNew.AbsenceDays)}");
            }
            if (objOld.VisitMilestone != objNew.VisitMilestone)
            {
                objPartes.Add($"A cada quantas visitas: {objOld.VisitMilestone?.ToString() ?? "—"} → {objNew.VisitMilestone?.ToString() ?? "—"}");
            }

            return string.Join("; ", objPartes);
        }

        private static string Json<T>(T objValue) => JsonSerializer.Serialize(objValue, s_objJson);

        private static string Data(DateOnly? dtValue) => dtValue?.ToString("dd/MM/yyyy") ?? "—";

        private static string Dias(int? iValue) => iValue is int iDias ? $"{iDias} dias" : "—";

        private static string Recorrencia(string? sValue) => sValue switch
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
