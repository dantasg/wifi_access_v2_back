namespace AccessWifiService
{
    /// <summary>Envio de e-mail com um anexo opcional (o relatório em CSV).</summary>
    public interface IEmailSender
    {
        Task SendAsync(
            string sToEmail,
            string sSubject,
            string sBody,
            byte[]? objAttachment,
            string? sAttachmentName,
            CancellationToken objCancellationToken = default);
    }
}
