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
        string? Error)
    {
        public static CampaignRunDto FromEntity(CampaignRun objRun)
        {
            int iProcessados = objRun.SentCount + objRun.SimulatedCount + objRun.FailedCount
                + objRun.IgnoredCount + objRun.CancelledCount;
            return new CampaignRunDto(
                objRun.Id, objRun.IDCampaign, objRun.VersionNumber, objRun.ScheduledFor, objRun.LocalDate,
                objRun.Status, objRun.Simulation, objRun.TotalCount, objRun.SentCount, objRun.SimulatedCount,
                objRun.FailedCount, objRun.IgnoredCount, objRun.CancelledCount,
                Math.Max(0, objRun.TotalCount - iProcessados),
                objRun.CreatedAt, objRun.StartedAt, objRun.FinishedAt, objRun.Error);
        }
    }

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
        DateTime UpdatedAt);

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
    public record CampaignRecipientDto(
        long Id, string Phone, string Name, string Message, string Status, string? Reason, int? Milestone,
        DateTime? ProcessedAt, string Unit, string Instagram, string Info, DateOnly? EventDate);

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
        DateTime? SentAt)
    {
        public static CampaignDeliveryDto FromEntity(CampaignDelivery objDelivery)
        {
            return new CampaignDeliveryDto(
                objDelivery.Id, objDelivery.IDUnit, objDelivery.UnitName, objDelivery.Email, objDelivery.Status,
                objDelivery.RecipientCount, objDelivery.Attempts, objDelivery.NextAttemptAt, objDelivery.Error,
                objDelivery.FileName, objDelivery.CreatedAt, objDelivery.SentAt);
        }
    }

    public record PagedDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

    /// <summary>Prévia de alcance: quantos clientes a campanha pegaria hoje, com uma mensagem de exemplo.</summary>
    public record AudiencePreviewRequest(string Kind, CampaignConfig Config, Guid? CampaignId = null);

    public record AudiencePreviewDto(int Count, DateOnly Date, string? SampleName, string? SampleMessage);
}
