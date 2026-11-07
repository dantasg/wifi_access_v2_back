using Models.Campaigns;
using Models.DataBase;

namespace AccessWifi.Api.Features.Campaigns
{
    /// <summary>
    /// Um tipo de campanha para a empresa: se está liberado (D6), se já existe a de sistema e se já está
    /// disponível (Available = false: "em breve", D23).
    /// </summary>
    public record CampaignCatalogItemDto(
        string Kind, string Label, bool IsSystem, bool Enabled, Guid? CampaignId, bool Available);

    public record CampaignRunDto(
        Guid Id,
        Guid CampaignId,
        int VersionNumber,
        DateTime ScheduledFor,
        DateOnly LocalDate,
        string Status,
        bool Simulation,
        int Total,
        int Sent,
        int Simulated,
        int Failed,
        int Ignored,
        int Cancelled,
        int Pending,
        DateTime CreatedAt,
        DateTime? StartedAt,
        DateTime? FinishedAt,
        string? Error,
        CampaignRunWhatsAppDto? WhatsApp = null)
    {
        public static CampaignRunDto FromEntity(CampaignRun objRun)
        {
            int iProcessed = objRun.SentCount + objRun.SimulatedCount + objRun.FailedCount
                + objRun.IgnoredCount + objRun.CancelledCount;
            return new CampaignRunDto(
                objRun.Id, objRun.IDCampaign, objRun.VersionNumber, objRun.ScheduledFor, objRun.LocalDate,
                objRun.Status, objRun.Simulation, objRun.TotalCount, objRun.SentCount, objRun.SimulatedCount,
                objRun.FailedCount, objRun.IgnoredCount, objRun.CancelledCount,
                Math.Max(0, objRun.TotalCount - iProcessed),
                objRun.CreatedAt, objRun.StartedAt, objRun.FinishedAt, objRun.Error);
        }

        /// <summary>
        /// A execução vista por um usuário de unidade: os números contam só os destinatários das
        /// unidades dele (objByStatus = quantos destinatários em cada situação).
        /// </summary>
        public static CampaignRunDto ForUnits(CampaignRun objRun, IReadOnlyDictionary<string, int> objByStatus)
        {
            int CountOf(string sStatus) => objByStatus.GetValueOrDefault(sStatus);
            return new CampaignRunDto(
                objRun.Id, objRun.IDCampaign, objRun.VersionNumber, objRun.ScheduledFor, objRun.LocalDate,
                objRun.Status, objRun.Simulation, objByStatus.Values.Sum(),
                CountOf(CampaignRecipientStatus.Sent), CountOf(CampaignRecipientStatus.Simulated),
                CountOf(CampaignRecipientStatus.Failed), CountOf(CampaignRecipientStatus.Ignored),
                CountOf(CampaignRecipientStatus.Cancelled), CountOf(CampaignRecipientStatus.Pending),
                objRun.CreatedAt, objRun.StartedAt, objRun.FinishedAt, objRun.Error);
        }
    }

    /// <summary>
    /// O WhatsApp automático numa execução (PROPOSTA_WHATSAPP.md, parte 3). Counts: quantos clientes em cada situação
    /// (Pending, Skipped, Accepted, Sent, Delivered, Read, Failed). VersionNumber: a versão cujo texto saiu.
    /// WaitUntil: até quando o e-mail ao gestor espera os avisos da Meta.
    /// </summary>
    public record CampaignRunWhatsAppDto(
        IReadOnlyDictionary<string, int> Counts, string? Error, int? VersionNumber, DateTime? WaitUntil);

    public record CampaignSummaryDto(
        Guid Id,
        string Kind,
        string KindLabel,
        string Name,
        string Status,
        bool KindEnabled,
        string Channel,
        string SendTime,
        int CurrentVersion,
        DateTime? NextRunAt,
        CampaignRunDto? LastRun,
        DateTime UpdatedAt,
        CampaignWhatsAppDto? WhatsApp = null);

    /// <summary>
    /// O modelo da Meta do texto atual da campanha (PROPOSTA_WHATSAPP.md, parte 2). Só vem quando a empresa tem o
    /// WhatsApp configurado. FallbackVersion = enquanto o texto atual não está aprovado, sai o texto aprovado
    /// desta versão anterior (nulo = nenhum aprovado ainda).
    /// </summary>
    public record CampaignWhatsAppDto(string Status, string Reason, string TemplateName, int? FallbackVersion);

    public record CampaignDetailDto(
        Guid Id,
        string Kind,
        string KindLabel,
        string Name,
        string Status,
        bool KindEnabled,
        int CurrentVersion,
        DateTime? NextRunAt,
        CampaignConfig Config,
        DateTime CreatedAt,
        DateTime UpdatedAt)
    {
        public static CampaignDetailDto FromEntity(Campaign objCampaign, bool bKindEnabled)
        {
            return new CampaignDetailDto(
                objCampaign.Id, objCampaign.Kind, CampaignKind.Label(objCampaign.Kind), objCampaign.Name,
                objCampaign.Status, bKindEnabled, objCampaign.CurrentVersion, objCampaign.NextRunAt,
                CampaignConfig.FromJson(objCampaign.ConfigJson), objCampaign.CreatedAt, objCampaign.UpdatedAt);
        }
    }

    /// <summary>Criar (com o tipo) ou editar (o tipo é ignorado: não muda depois de criada).</summary>
    public record SaveCampaignRequest(string? Kind, string Name, CampaignConfig Config);

    public record CampaignVersionDto(
        int Number, string Name, string Changes, DateTime CreatedAt, string Username, CampaignConfig Config);

    /// <param name="RunDate">Ação sobre uma execução: o dia dela (fuso da empresa). Nulo nas ações da campanha.</param>
    public record CampaignEventDto(string Action, DateTime CreatedAt, string Username, Guid? RunId, DateOnly? RunDate);

    /// <param name="Unit">Unidade cujo gerente recebeu o cliente no PDF (D17).</param>
    /// <param name="Info">A coluna de informação do PDF (ex.: "sex, 16/10 · 29 anos").</param>
    /// <param name="WhatsAppStatus">A mensagem pelo WhatsApp ("" = execução sem WhatsApp); WhatsAppError, o motivo.</param>
    public record CampaignRecipientDto(
        long Id, string Phone, string Name, string Message, string Status, string? Reason, int? Milestone,
        DateTime? ProcessedAt, string Unit, string Instagram, string Info, DateOnly? EventDate,
        string WhatsAppStatus = "", string? WhatsAppError = null,
        CampaignRecipientReplyDto? LastReply = null, int ReplyCount = 0);

    /// <summary>A última resposta do cliente a esta execução pelo WhatsApp (D10, D14).</summary>
    public record CampaignRecipientReplyDto(string Text, DateTime SentAt, bool IsOptOut);

    /// <summary>O e-mail de uma unidade numa execução: para onde foi, quando, e o motivo de uma falha.</summary>
    public record CampaignDeliveryDto(
        Guid Id,
        Guid? UnitId,
        string UnitName,
        string Email,
        string Status,
        int RecipientCount,
        int Attempts,
        DateTime? NextAttemptAt,
        string? Error,
        string FileName,
        DateTime CreatedAt,
        DateTime? SentAt,
        bool IsResend,
        string RequestedBy)
    {
        public static CampaignDeliveryDto FromEntity(CampaignDelivery objDelivery)
        {
            return new CampaignDeliveryDto(
                objDelivery.Id, objDelivery.IDUnit, objDelivery.UnitName, objDelivery.Email, objDelivery.Status,
                objDelivery.RecipientCount, objDelivery.Attempts, objDelivery.NextAttemptAt, objDelivery.Error,
                objDelivery.FileName, objDelivery.CreatedAt, objDelivery.SentAt, objDelivery.IsResend,
                objDelivery.RequestedBy);
        }
    }

    public record PagedDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

    /// <summary>Prévia de alcance: quantos clientes a campanha pegaria hoje, com uma mensagem de exemplo.</summary>
    public record AudiencePreviewRequest(string Kind, CampaignConfig Config, Guid? CampaignId = null);

    public record AudiencePreviewDto(int Count, DateOnly Date, string? SampleName, string? SampleMessage);
}
