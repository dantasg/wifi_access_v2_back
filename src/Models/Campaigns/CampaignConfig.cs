using System.Text.Json;
using System.Text.RegularExpressions;
using Models.DataBase;

namespace Models.Campaigns
{
    /// <summary>
    /// Configuração de uma campanha, guardada em JSON na campanha e em cada versão. Tudo o que muda o
    /// comportamento de um disparo está aqui — é a "foto" com que uma execução roda (D13).
    /// </summary>
    public sealed record CampaignConfig
    {
        /// <summary>Ver <see cref="CampaignChannel"/>.</summary>
        public string Channel { get; init; } = CampaignChannel.WhatsApp;

        /// <summary>Texto com campos como {primeiro_nome} (ver <see cref="CampaignMessage"/>).</summary>
        public string Message { get; init; } = "";

        /// <summary>Horário do disparo no fuso da empresa, "HH:mm".</summary>
        public string SendTime { get; init; } = "09:00";

        /// <summary>Só campanha filtrada: quando (início, fim, repetição).</summary>
        public CampaignScheduleConfig? Schedule { get; init; }

        /// <summary>Só campanha filtrada: quem recebe. Nulo ou vazio = todos os clientes da empresa.</summary>
        public CampaignFilters? Filters { get; init; }

        /// <summary>Só campanha filtrada: não manda de novo para quem recebeu desta campanha nos últimos N dias.</summary>
        public int? ResendAfterDays { get; init; }

        /// <summary>Sentimos sua falta: dias sem voltar para receber.</summary>
        public int? AbsenceDays { get; init; }

        /// <summary>Cliente frequente: a cada quantas visitas (5 = na 5ª, 10ª, 15ª…).</summary>
        public int? VisitMilestone { get; init; }

        public const int MaxMessageChars = 1000;

        private static readonly JsonSerializerOptions s_objJsonOptions = new(JsonSerializerDefaults.Web);

        public string ToJson() => JsonSerializer.Serialize(this, s_objJsonOptions);

        public static CampaignConfig FromJson(string? sJson)
        {
            if (string.IsNullOrWhiteSpace(sJson))
            {
                return new CampaignConfig();
            }
            return JsonSerializer.Deserialize<CampaignConfig>(sJson, s_objJsonOptions) ?? new CampaignConfig();
        }

        public TimeOnly SendTimeValue() =>
            TimeOnly.TryParseExact(SendTime, "HH:mm", out TimeOnly objTime) ? objTime : new TimeOnly(9, 0);
    }

    public static class CampaignRecurrence
    {
        public const string Once = "Once";
        public const string Daily = "Daily";
        public const string Weekly = "Weekly";

        /// <summary>No dia do mês da data de início; em mês mais curto, no último dia.</summary>
        public const string Monthly = "Monthly";

        /// <summary>No dia e mês da data de início; 29/02 vira 28/02 nos anos não bissextos.</summary>
        public const string Yearly = "Yearly";

        public static bool IsValid(string? sValue) => sValue is Once or Daily or Weekly or Monthly or Yearly;
    }

    public sealed record CampaignScheduleConfig
    {
        public string Recurrence { get; init; } = CampaignRecurrence.Once;
        public DateOnly StartDate { get; init; }

        /// <summary>Último dia com disparo (inclusive). Nulo = sem fim.</summary>
        public DateOnly? EndDate { get; init; }

        /// <summary>Semanal: dias da semana (0 = domingo … 6 = sábado). Vazio = o dia da semana do início.</summary>
        public IReadOnlyList<int>? DaysOfWeek { get; init; }
    }

    /// <summary>Filtros combinados com "e". Campo nulo = não filtra por ele.</summary>
    public sealed record CampaignFilters
    {
        /// <summary>Clientes que já visitaram alguma destas unidades.</summary>
        public IReadOnlyList<Guid>? UnitIds { get; init; }
        public int? AgeMin { get; init; }
        public int? AgeMax { get; init; }

        /// <summary>Mês de aniversário (1–12).</summary>
        public IReadOnlyList<int>? BirthMonths { get; init; }
        public DateOnly? FirstVisitFrom { get; init; }
        public DateOnly? FirstVisitTo { get; init; }

        /// <summary>Veio nos últimos N dias.</summary>
        public int? LastVisitWithinDays { get; init; }

        /// <summary>Não vem há mais de N dias.</summary>
        public int? LastVisitOlderThanDays { get; init; }
        public int? VisitsMin { get; init; }
        public bool? HasInstagram { get; init; }
    }

    /// <summary>Confere uma configuração antes de gravar. Devolve a mensagem de erro ou null.</summary>
    public static partial class CampaignConfigValidator
    {
        [GeneratedRegex("^([01][0-9]|2[0-3]):[0-5][0-9]$")]
        private static partial Regex SendTimeRegex();

        public static string? Validate(string sKind, CampaignConfig objConfig)
        {
            if (!CampaignChannel.IsValid(objConfig.Channel))
            {
                return "Canal inválido.";
            }
            if (objConfig.Channel == CampaignChannel.Instagram)
            {
                // D15: a API do Instagram não deixa a empresa iniciar conversa.
                return "O Instagram ainda não está disponível para campanhas.";
            }
            if (string.IsNullOrWhiteSpace(objConfig.Message))
            {
                return "Escreva a mensagem da campanha.";
            }
            if (objConfig.Message.Length > CampaignConfig.MaxMessageChars)
            {
                return $"A mensagem pode ter no máximo {CampaignConfig.MaxMessageChars} caracteres.";
            }
            if (objConfig.SendTime is null || !SendTimeRegex().IsMatch(objConfig.SendTime))
            {
                return "Horário inválido (use HH:mm, ex.: 09:00).";
            }

            if (sKind == CampaignKind.WeMissYou && objConfig.AbsenceDays is not (>= 7 and <= 730))
            {
                return "Informe há quantos dias o cliente não volta (de 7 a 730).";
            }
            if (sKind == CampaignKind.FrequentCustomer && objConfig.VisitMilestone is not (>= 2 and <= 1000))
            {
                return "Informe a cada quantas visitas o cliente recebe (de 2 a 1000).";
            }

            if (sKind != CampaignKind.Filtered)
            {
                return null;
            }

            CampaignScheduleConfig? objSchedule = objConfig.Schedule;
            if (objSchedule is null || !CampaignRecurrence.IsValid(objSchedule.Recurrence))
            {
                return "Escolha quando a campanha dispara (uma vez, diária, semanal, mensal ou anual).";
            }
            if (objSchedule.StartDate == default)
            {
                return "Informe a data de início.";
            }
            if (objSchedule.EndDate is DateOnly dtEnd && dtEnd < objSchedule.StartDate)
            {
                return "A data de fim não pode ser antes da data de início.";
            }
            if (objSchedule.DaysOfWeek is { Count: > 0 } objDays && objDays.Any(iDay => iDay is < 0 or > 6))
            {
                return "Dia da semana inválido.";
            }
            if (objConfig.ResendAfterDays is < 1 or > 3650)
            {
                return "O intervalo para mandar de novo deve ficar entre 1 e 3650 dias.";
            }

            CampaignFilters? objFilters = objConfig.Filters;
            if (objFilters is null)
            {
                return null;
            }
            if (objFilters.AgeMin is < 0 or > 120 || objFilters.AgeMax is < 0 or > 120)
            {
                return "Idade deve ficar entre 0 e 120.";
            }
            if (objFilters.AgeMin is int iMin && objFilters.AgeMax is int iMax && iMin > iMax)
            {
                return "A idade mínima não pode ser maior que a máxima.";
            }
            if (objFilters.BirthMonths is { Count: > 0 } objMonths && objMonths.Any(iMonth => iMonth is < 1 or > 12))
            {
                return "Mês de aniversário inválido.";
            }
            if (objFilters.FirstVisitFrom is DateOnly dtFrom && objFilters.FirstVisitTo is DateOnly dtTo && dtFrom > dtTo)
            {
                return "O período de cadastro está invertido.";
            }
            if (objFilters.LastVisitWithinDays is < 1 or > 3650 || objFilters.LastVisitOlderThanDays is < 1 or > 3650)
            {
                return "Os dias da última visita devem ficar entre 1 e 3650.";
            }
            if (objFilters.VisitsMin is < 1 or > 100000)
            {
                return "O número mínimo de visitas deve ser 1 ou mais.";
            }

            return null;
        }
    }
}
