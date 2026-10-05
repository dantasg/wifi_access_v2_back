using System.Net;
using System.Net.Mail;

namespace AccessWifi.Ops
{
    /// <summary>
    /// E-mail dos avisos, pela conta configurada em ops.env. Usa STARTTLS (porta 587, ex.: Gmail com senha de
    /// app). A porta 465 (TLS direto) não é suportada pelo SmtpClient do .NET — use a 587.
    /// </summary>
    public static class AlertEmail
    {
        public static async Task SendAsync(OpsSettings objSettings, string sSubject, string sBody)
        {
            if (!objSettings.HasEmail)
            {
                throw new InvalidOperationException("e-mail não configurado (rode: accesswifi-ops configurar)");
            }

            await Retry.RunAsync(async () =>
            {
                using SmtpClient objClient = new SmtpClient(objSettings.Get(OpsSettings.KeySmtpHost), objSettings.SmtpPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(
                        objSettings.Get(OpsSettings.KeySmtpUser), objSettings.Get(OpsSettings.KeySmtpPassword)),
                    Timeout = 30_000,
                };
                using MailMessage objMessage = new MailMessage
                {
                    From = new MailAddress(objSettings.SmtpFrom, objSettings.SmtpFromName),
                    Subject = $"[AccessWifi] {sSubject}",
                    Body = sBody,
                    IsBodyHtml = false,
                    BodyEncoding = System.Text.Encoding.UTF8,
                    SubjectEncoding = System.Text.Encoding.UTF8,
                };
                foreach (string sTo in objSettings.AlertEmails)
                {
                    objMessage.To.Add(sTo);
                }
                await objClient.SendMailAsync(objMessage);
            });
        }
    }
}
