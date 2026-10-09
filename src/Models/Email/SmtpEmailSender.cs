using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;

namespace Models.Email
{
    /// <summary>
    /// Envio via SMTP. Lê as credenciais da tabela Configuration (chaves SMTP_*). Sem SMTP configurado,
    /// lança erro: quem chama registra a falha (o relatório não marca como enviado, a campanha mostra o
    /// motivo no histórico) em vez de achar que o e-mail saiu.
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
                throw new InvalidOperationException(
                    "O SMTP não está configurado (painel → Configurações do sistema).");
            }

            using SmtpClient objClient = new SmtpClient(objSmtp.Host, objSmtp.Port)
            {
                Credentials = new NetworkCredential(objSmtp.Username, objSmtp.Password),
                EnableSsl = objSmtp.UseStartTls,
                // Servidor errado ou porta bloqueada não pode prender o serviço (nem o teste do painel).
                Timeout = 30_000,
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

                string sName = sAttachmentName ?? "anexo.csv";
                // Com o tipo certo, o celular abre o PDF direto do e-mail.
                string sKind = Path.GetExtension(sName).ToLowerInvariant() switch
                {
                    ".pdf" => MediaTypeNames.Application.Pdf,
                    ".csv" => MediaTypeNames.Text.Csv,
                    _ => MediaTypeNames.Application.Octet,
                };
                Attachment objMailAttachment = new Attachment(objStream, sName, sKind);

                objMessage.Attachments.Add(objMailAttachment);
            }

            await objClient.SendMailAsync(objMessage, objCancellationToken);

            _objLogger.LogInformation(
                "E-mail enviado para {To}, assunto '{Subject}', anexo {Bytes} bytes (host {Host}).",
                sToEmail, sSubject, objAttachment?.Length ?? 0, objSmtp.Host);
        }
    }
}
