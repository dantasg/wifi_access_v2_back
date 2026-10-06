using AccessWifiService;

namespace AccessWifi.Api.Tests;

/// <summary>Dublê do envio de e-mail: guarda o que seria enviado e pode falhar nas primeiras tentativas.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    public sealed record Email(string To, string Subject, string Body, byte[]? Attachment, string? AttachmentName);

    public List<Email> Enviados { get; } = [];

    /// <summary>Quantas chamadas ainda falham (simula o SMTP fora do ar).</summary>
    public int FalharVezes { get; set; }

    public Task SendAsync(
        string sToEmail, string sSubject, string sBody,
        byte[]? objAttachment, string? sAttachmentName, CancellationToken objCancellationToken = default)
    {
        if (FalharVezes > 0)
        {
            FalharVezes--;
            throw new InvalidOperationException("SMTP fora do ar");
        }
        Enviados.Add(new Email(sToEmail, sSubject, sBody, objAttachment, sAttachmentName));
        return Task.CompletedTask;
    }
}
