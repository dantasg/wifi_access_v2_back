using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AccessWifiService
{
    /// <summary>
    /// Envio via SMTP. A implementação do envio em si será feita depois — a fiação
    /// (opções da seção "Smtp", logging e injeção) já está pronta.
    /// </summary>
    public class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpOptions _objOptions;
        private readonly ILogger<SmtpEmailSender> _objLogger;

        public SmtpEmailSender(IOptions<SmtpOptions> objOptions, ILogger<SmtpEmailSender> objLogger)
        {
            _objOptions = objOptions.Value;
            _objLogger = objLogger;
        }

        public Task SendAsync(
            string sToEmail,
            string sSubject,
            string sBody,
            byte[]? objAttachment,
            string? sAttachmentName,
            CancellationToken objCancellationToken = default)
        {
            // TODO: implementar o envio SMTP usando _objOptions (Host/Port/Username/Password/From).
            _objLogger.LogInformation(
                "Envio SMTP ainda não implementado — e-mail para {To}, assunto '{Subject}', anexo {Bytes} bytes.",
                sToEmail, sSubject, objAttachment?.Length ?? 0);
            return Task.CompletedTask;
        }
    }
}
