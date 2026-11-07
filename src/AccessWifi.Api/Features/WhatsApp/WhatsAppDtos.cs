using Models.DataBase;

namespace AccessWifi.Api.Features.WhatsApp
{
    /// <summary>
    /// O WhatsApp da empresa para a tela (PROPOSTA_WHATSAPP.md). Nenhum segredo sai daqui: o token, a chave
    /// secreta do app e o código de verificação só aparecem como "existe ou não" (Has*), como a senha do SMTP.
    /// </summary>
    public record WhatsAppSettingsDto(
        bool Enabled,
        DateTime? ChangedAt,
        string ChangedBy,
        string PhoneNumberId,
        string BusinessAccountId,
        bool HasAccessToken,
        bool HasAppSecret,
        bool HasVerifyToken,
        string DisplayPhoneNumber,
        string DisplayName,
        string QualityRating,
        string MessagingLimit,
        DateTime? CheckedAt,
        bool CheckPassed,
        string LastError,
        string WebhookUrl = "",
        DateTime? WebhookVerifiedAt = null,
        DateTime? LastWebhookAt = null)
    {
        public static WhatsAppSettingsDto From(Company objCompany)
        {
            CompanyWhatsApp objWhatsApp = objCompany.WhatsApp;
            return new WhatsAppSettingsDto(
                Enabled: objCompany.WhatsAppEnabled,
                ChangedAt: objCompany.WhatsAppChangedAt,
                ChangedBy: objCompany.WhatsAppChangedBy,
                PhoneNumberId: objWhatsApp.PhoneNumberId,
                BusinessAccountId: objWhatsApp.BusinessAccountId,
                HasAccessToken: objWhatsApp.AccessToken.Length > 0,
                HasAppSecret: objWhatsApp.AppSecret.Length > 0,
                HasVerifyToken: objWhatsApp.VerifyToken.Length > 0,
                DisplayPhoneNumber: objWhatsApp.DisplayPhoneNumber,
                DisplayName: objWhatsApp.DisplayName,
                QualityRating: objWhatsApp.QualityRating,
                MessagingLimit: objWhatsApp.MessagingLimit,
                CheckedAt: objWhatsApp.CheckedAt,
                CheckPassed: objWhatsApp.CheckPassed,
                LastError: objWhatsApp.LastError);
        }
    }

    /// <summary>
    /// Os ids são sempre enviados (vazio = apagar). Os segredos são só de ida: nulo ou vazio = manter o atual;
    /// texto = trocar (vai cifrado para o banco).
    /// </summary>
    public record WhatsAppSettingsRequest(
        string? PhoneNumberId,
        string? BusinessAccountId,
        string? AccessToken,
        string? AppSecret,
        string? VerifyToken);

    public record WhatsAppEnabledRequest(bool Enabled);

    /// <summary>
    /// O WhatsApp de atendimento de uma unidade (D10): vai no fim de cada mensagem automática. StorePhone só com
    /// dígitos (DDD + número); "" = não cadastrado.
    /// </summary>
    public record WhatsAppUnitDto(Guid Id, string Name, bool Active, string StorePhone);

    /// <summary>StorePhone vazio apaga; com máscara ou +55 também vale (o servidor guarda só DDD + número).</summary>
    public record WhatsAppUnitPhoneRequest(Guid UnitId, string? StorePhone);

    /// <summary>
    /// Um modelo de mensagem da Meta (parte 2): de qual campanha, a situação e o motivo, e o texto como o cliente
    /// vê, com exemplos no lugar dos campos. IsCurrent = é o texto atual da campanha (os outros são de versões
    /// anteriores).
    /// </summary>
    public record WhatsAppTemplateDto(
        Guid Id,
        string Name,
        Guid CampaignId,
        string CampaignName,
        int VersionNumber,
        bool IsCurrent,
        string Status,
        string MetaStatus,
        string Category,
        string Reason,
        string LastError,
        string Preview,
        DateTime CreatedAt,
        DateTime? SubmittedAt,
        DateTime? StatusChangedAt,
        DateTime? CheckedAt);

    /// <summary>Enviar teste: um texto aprovado e o celular (com máscara ou +55).</summary>
    public record WhatsAppTestMessageRequest(Guid TemplateId, string? Phone);

    /// <summary>O teste saiu: para qual número e o texto como foi.</summary>
    public record WhatsAppTestMessageDto(string Phone, string Text);
}
