namespace AccessWifi.Api.Features.SystemSettings
{
    /// <summary>
    /// A conta de e-mail que o sistema inteiro usa para enviar (relatório mensal e PDF das campanhas). A senha
    /// nunca é devolvida: HasPassword só diz se existe uma guardada, para a tela mostrar "configurada".
    /// </summary>
    public record SmtpSettingsDto(
        string Host,
        int Port,
        string Username,
        bool HasPassword,
        string FromEmail,
        string FromName,
        bool UseStartTls);

    public record SystemSettingsDto(SmtpSettingsDto Smtp);

    /// <summary>
    /// Password nulo = manter a atual; texto = trocar. Host vazio = sem SMTP (nada sai por e-mail).
    /// Port e UseStartTls nulos = 587 e STARTTLS ligado.
    /// </summary>
    public record SmtpSettingsRequest(
        string? Host,
        int? Port,
        string? Username,
        string? Password,
        string? FromEmail,
        string? FromName,
        bool? UseStartTls);

    public record UpdateSystemSettingsRequest(SmtpSettingsRequest Smtp);

    public record SmtpTestRequest(string To);

    /// <summary>Resultado do "Enviar e-mail de teste". Sucesso falso não é erro HTTP: é o resultado do teste.</summary>
    public record SmtpTestResponse(bool Success, string Message);
}
