namespace AccessWifi.Api.Features.WhatsApp
{
    /// <summary>
    /// Um cliente na lista de Conversas (PROPOSTA_WHATSAPP.md §15.3): a última mensagem, de qualquer lado, e quantas
    /// foram enviadas e respondidas.
    /// </summary>
    /// <param name="LastDirection">"out" = a última foi nossa; "in" = do cliente.</param>
    /// <param name="LastStatus">Situação da última enviada (Accepted, Sent, Delivered, Read, Failed); "" se a última foi dele.</param>
    public record WhatsAppConversationDto(
        Guid CustomerId,
        string Name,
        string Phone,
        string Unit,
        DateTime LastAt,
        string LastDirection,
        string LastText,
        string LastStatus,
        DateTime? OptedOutAt,
        int SentCount,
        int ReplyCount,
        string WhatsAppUrl);

    /// <summary>O histórico completo de um cliente, separado por campanha (D14).</summary>
    public record WhatsAppConversationDetailDto(
        Guid CustomerId,
        string Name,
        string Phone,
        string Unit,
        DateTime? OptedOutAt,
        string WhatsAppUrl,
        IReadOnlyList<WhatsAppConversationGroupDto> Groups);

    /// <summary>
    /// Uma mensagem enviada e as respostas que vieram até a mensagem seguinte. Message nulo = respostas sem campanha
    /// antes (o cliente escreveu antes de receber qualquer campanha).
    /// </summary>
    public record WhatsAppConversationGroupDto(WhatsAppSentDto? Message, IReadOnlyList<WhatsAppReplyItemDto> Replies);

    /// <summary>Uma mensagem enviada: de qual campanha e versão, o texto como o cliente leu e a situação.</summary>
    public record WhatsAppSentDto(
        long Id,
        string CampaignName,
        DateOnly? RunDate,
        int? VersionNumber,
        string Text,
        string Status,
        string ErrorMessage,
        DateTime CreatedAt,
        DateTime? SentAt,
        DateTime? DeliveredAt,
        DateTime? ReadAt,
        DateTime? FailedAt,
        bool IsTest,
        string Username);

    public record WhatsAppReplyItemDto(long Id, string Text, DateTime SentAt, bool IsOptOut, string ProfileName);

    /// <summary>
    /// Uma linha do Registro: enviada ("out") ou recebida ("in"). Para as recebidas, Status vem vazio e IsOptOut diz
    /// se era um "SAIR".
    /// </summary>
    public record WhatsAppLogItemDto(
        string Direction,
        long Id,
        DateTime At,
        Guid? CustomerId,
        string CustomerName,
        string Phone,
        string Unit,
        string CampaignName,
        string Text,
        string Status,
        string Error,
        bool IsTest,
        bool IsOptOut,
        string WhatsAppUrl);

    /// <summary>
    /// O detalhe de uma mensagem enviada: cada mudança de situação, a cobrança, o motivo do erro, e o que foi pedido à
    /// Meta (sem o token) e o que ela respondeu.
    /// </summary>
    public record WhatsAppMessageDetailDto(
        WhatsAppSentDto Message,
        Guid? CustomerId,
        string CustomerName,
        string ToPhone,
        string FromDisplayPhone,
        string TemplateName,
        string ParametersJson,
        string MetaMessageId,
        int HttpStatus,
        long DurationMs,
        string PricingCategory,
        string PricingType,
        bool? Billable,
        string? RequestJson,
        string? ResponseJson,
        IReadOnlyList<WhatsAppMessageEventDto> Events);

    public record WhatsAppMessageEventDto(
        string Status, DateTime? At, int? ErrorCode, string ErrorMessage, string PricingCategory, bool? Billable,
        DateTime ReceivedAt);

    /// <summary>Um acontecimento da conta (aba Conta): o que foi, quem fez, quando e, nas idas à Meta, o pedido e a resposta.</summary>
    public record WhatsAppAccountEventDto(
        long Id, string Kind, string Username, string Details, string? RequestJson, string? ResponseJson,
        DateTime CreatedAt);
}
