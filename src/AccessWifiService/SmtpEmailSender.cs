using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;

namespace AccessWifiService
{
    /// <summary>
    /// Envio via SMTP. Lê as credenciais da tabela Configuration (chaves SMTP_*).
    /// A implementação do envio em si será feita depois — a fiação já está pronta.
    /// </summary>
    public class SmtpEmailSender : IEmailSender
    {
        private readonly ConfigurationReader _objConfigReader;
        private readonly ILogger<SmtpEmailSender> _objLogger;

        public SmtpEmailSender(ConfigurationReader objConfigReader, ILogger<SmtpEmailSender> objLogger)
        {
            _objConfigReader = objConfigReader;
            _objLogger = objLogger;
        }

        public async Task SendAsync(string sToEmail, string sSubject, string sBody, byte[]? objAttachment, string? sAttachmentName, CancellationToken objCancellationToken = default)
        {
            SmtpOptions objSmtp = await _objConfigReader.GetSmtpAsync(objCancellationToken);

            if (string.IsNullOrWhiteSpace(objSmtp.Host))
            {
                _objLogger.LogWarning(
                    "SMTP não configurado na tabela Configuration (SMTP_HOST vazio) — e-mail para {To} não enviado.",
                    sToEmail);
                return;
            }

            using SmtpClient objClient = new SmtpClient(objSmtp.Host, objSmtp.Port)
            {
                Credentials = new NetworkCredential(objSmtp.Username, objSmtp.Password),
                EnableSsl = objSmtp.UseStartTls
            };

            using MailMessage objMessage = new MailMessage
            {
                From = new MailAddress(objSmtp.FromEmail, objSmtp.FromName),
                Subject = sSubject,
                Body = sBody,
                IsBodyHtml = false
            };

            objMessage.To.Add(sToEmail);

            // Adiciona o anexo se existir
            if (objAttachment != null)
            {
                MemoryStream objStream = new MemoryStream(objAttachment);

                Attachment objMailAttachment = new Attachment(
                    objStream,
                    sAttachmentName ?? "anexo.csv",
                    MediaTypeNames.Application.Octet);

                objMessage.Attachments.Add(objMailAttachment);
            }

            await objClient.SendMailAsync(objMessage, objCancellationToken);

            _objLogger.LogInformation(
                "E-mail enviado para {To}, assunto '{Subject}', anexo {Bytes} bytes (host {Host}).",
                sToEmail, sSubject, objAttachment?.Length ?? 0, objSmtp.Host);
        }
    }
}
