using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Models.DataBase;
using Models.Email;
using Models.Persistence;

namespace AccessWifiService.SiteContacts
{
    public sealed class SiteContactOptions
    {
        public const string SectionName = "SiteContact";

        /// <summary>Quem recebe o aviso de contato novo (PROPOSTA_SITE.md, D10). Vazio = não avisa.</summary>
        public string NotifyEmail { get; set; } = "";

        /// <summary>De quanto em quanto tempo o serviço procura contatos novos.</summary>
        public int TickSeconds { get; set; } = 60;

        /// <summary>Tentativas de mandar o aviso antes de desistir (o contato continua na tela do painel).</summary>
        public int MaxAttempts { get; set; } = 5;
    }

    /// <summary>
    /// Avisa por e-mail cada contato novo do site institucional (PROPOSTA_SITE.md §4). Quem chega pelo formulário fica
    /// gravado na hora pela API; aqui sai o e-mail, com nova tentativa na volta seguinte se o servidor de e-mail falhar.
    /// O contato aparece na tela "Contatos do site" mesmo que o aviso não saia.
    /// </summary>
    public sealed class SiteContactNotifier
    {
        private const int BatchSize = 20;

        private readonly AppDbContext _objDbContext;
        private readonly IEmailSender _objEmailSender;
        private readonly SiteContactOptions _objOptions;
        private readonly ILogger<SiteContactNotifier> _objLogger;

        public SiteContactNotifier(
            AppDbContext objDbContext,
            IEmailSender objEmailSender,
            IOptions<SiteContactOptions> objOptions,
            ILogger<SiteContactNotifier> objLogger)
        {
            _objDbContext = objDbContext;
            _objEmailSender = objEmailSender;
            _objOptions = objOptions.Value;
            _objLogger = objLogger;
        }

        public async Task TickAsync(DateTime dtNowUtc, CancellationToken objCancellationToken = default)
        {
            string sTo = _objOptions.NotifyEmail.Trim();
            if (sTo.Length == 0)
            {
                return;
            }

            int iMaxAttempts = Math.Max(1, _objOptions.MaxAttempts);
            List<SiteContact> objPending = await _objDbContext.SiteContacts
                .Where(contact => contact.NotifiedAt == null && contact.NotifyAttempts < iMaxAttempts)
                .OrderBy(contact => contact.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(objCancellationToken);

            foreach (SiteContact objContact in objPending)
            {
                objContact.NotifyAttempts++;
                try
                {
                    await _objEmailSender.SendAsync(
                        sTo, BuildSubject(objContact), BuildBody(objContact), null, null, objCancellationToken);
                    objContact.NotifiedAt = dtNowUtc;
                    objContact.NotifyError = "";
                }
                catch (Exception objException) when (objException is not OperationCanceledException)
                {
                    objContact.NotifyError = objException.Message.Length <= 500 ? objException.Message : objException.Message[..500];
                    _objLogger.LogWarning(
                        "Contato do site: aviso por e-mail falhou (tentativa {Attempt} de {Max}): {Error}",
                        objContact.NotifyAttempts, iMaxAttempts, objContact.NotifyError);
                }
                await _objDbContext.SaveChangesAsync(objCancellationToken);
            }
        }

        public static string BuildSubject(SiteContact objContact) =>
            $"Novo contato do site: {objContact.Company} ({objContact.City}/{objContact.State})";

        public static string BuildBody(SiteContact objContact)
        {
            TimeZoneInfo objZone = CompanyTimeZone.Resolve(null);
            DateTime dtLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(objContact.CreatedAt, DateTimeKind.Utc), objZone);

            StringBuilder objBody = new StringBuilder();
            objBody.AppendLine("Novo pedido de contato pelo site do AccessWifi.");
            objBody.AppendLine();
            objBody.AppendLine($"Nome: {objContact.Name}");
            objBody.AppendLine($"Empresa: {objContact.Company}");
            objBody.AppendLine($"WhatsApp: {SiteContactChoices.FormatPhone(objContact.Phone)} (https://wa.me/55{objContact.Phone})");
            objBody.AppendLine($"E-mail: {(objContact.Email.Length > 0 ? objContact.Email : "não informado")}");
            objBody.AppendLine($"Cidade: {objContact.City}/{objContact.State}");
            objBody.AppendLine($"Lojas: {Label(SiteContactChoices.Stores, objContact.Stores)}");
            objBody.AppendLine($"Já usa UniFi: {Label(SiteContactChoices.UsesUnifi, objContact.UsesUnifi)}");
            if (objContact.Message.Length > 0)
            {
                objBody.AppendLine();
                objBody.AppendLine("Mensagem:");
                objBody.AppendLine(objContact.Message);
            }

            objBody.AppendLine();
            objBody.AppendLine("De onde veio:");
            objBody.AppendLine($"  Origem: {Origin(objContact)}");
            if (objContact.UtmCampaign.Length > 0)
            {
                objBody.AppendLine($"  Campanha: {objContact.UtmCampaign}");
            }
            if (objContact.UtmTerm.Length > 0)
            {
                objBody.AppendLine($"  Palavra-chave: {objContact.UtmTerm}");
            }
            if (objContact.UtmContent.Length > 0)
            {
                objBody.AppendLine($"  Anúncio: {objContact.UtmContent}");
            }
            if (objContact.LandingPage.Length > 0)
            {
                objBody.AppendLine($"  Página de entrada: {objContact.LandingPage}");
            }
            if (objContact.Referrer.Length > 0)
            {
                objBody.AppendLine($"  Site anterior: {objContact.Referrer}");
            }

            objBody.AppendLine();
            objBody.AppendLine($"Recebido em {dtLocal:dd/MM/yyyy} às {dtLocal:HH:mm}.");
            objBody.AppendLine("O contato fica no painel, em \"Contatos do site\", para acompanhar o andamento.");
            return objBody.ToString();
        }

        /// <summary>Resumo da origem: anúncio do Google/Meta, UTM ou acesso direto.</summary>
        public static string Origin(SiteContact objContact)
        {
            List<string> objParts = [];
            if (objContact.Gclid.Length > 0)
            {
                objParts.Add("anúncio do Google");
            }
            if (objContact.Fbclid.Length > 0)
            {
                objParts.Add("anúncio da Meta");
            }
            if (objContact.UtmSource.Length > 0)
            {
                objParts.Add(objContact.UtmMedium.Length > 0
                    ? $"{objContact.UtmSource} / {objContact.UtmMedium}"
                    : objContact.UtmSource);
            }
            return objParts.Count > 0 ? string.Join(", ", objParts) : "direto (sem anúncio)";
        }

        private static string Label(IReadOnlyDictionary<string, string> objChoices, string sKey) =>
            objChoices.TryGetValue(sKey, out string? sLabel) ? sLabel : sKey;
    }
}
