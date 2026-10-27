namespace Models.DataBase
{
    /// <summary>Andamento do contato na tela "Contatos do site" (PROPOSTA_SITE.md §4).</summary>
    public enum SiteContactStatus
    {
        New,
        Talking,
        Client,
        Discarded,
    }

    /// <summary>
    /// Um pedido de contato do site institucional (PROPOSTA_SITE.md §4, D9–D11): o que a pessoa preencheu, de onde ela
    /// veio (UTM, gclid, fbclid: é o que diz qual anúncio trouxe o contato) e o andamento da conversa.
    ///
    /// Diferente dos cadastros do portal, aqui o AccessWifi é o responsável pelos dados (não é cliente de uma loja). O
    /// contato é apagado 12 meses depois, a não ser que vire cliente (D11, LeadRetentionService). O aviso por e-mail é
    /// do serviço (SiteContactNotifier), para a página não esperar o servidor de e-mail.
    /// </summary>
    public class SiteContact
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = "";
        public string Company { get; set; } = "";

        /// <summary>WhatsApp, só dígitos, com DDD (10 ou 11 dígitos, sem o 55).</summary>
        public string Phone { get; set; } = "";

        /// <summary>Opcional ("" = não informou).</summary>
        public string Email { get; set; } = "";

        public string City { get; set; } = "";

        /// <summary>UF, 2 letras maiúsculas.</summary>
        public string State { get; set; } = "";

        /// <summary>Quantas lojas: uma das chaves de <see cref="SiteContactChoices.Stores"/>.</summary>
        public string Stores { get; set; } = "";

        /// <summary>Já usa UniFi: uma das chaves de <see cref="SiteContactChoices.UsesUnifi"/>.</summary>
        public string UsesUnifi { get; set; } = "";

        public string Message { get; set; } = "";

        /// <summary>Quando a pessoa marcou a autorização de contato (a mesma hora do envio).</summary>
        public DateTime ConsentAt { get; set; }

        // ------------------------------------------------ Origem (o site guarda no navegador e manda junto)
        public string UtmSource { get; set; } = "";
        public string UtmMedium { get; set; } = "";
        public string UtmCampaign { get; set; } = "";
        public string UtmTerm { get; set; } = "";
        public string UtmContent { get; set; } = "";

        /// <summary>Clique de anúncio do Google (gclid, gbraid ou wbraid).</summary>
        public string Gclid { get; set; } = "";

        /// <summary>Clique de anúncio da Meta.</summary>
        public string Fbclid { get; set; } = "";

        /// <summary>Primeira página vista na visita que trouxe a pessoa, com os parâmetros.</summary>
        public string LandingPage { get; set; } = "";

        /// <summary>Site de onde a pessoa veio (ex.: https://www.google.com/).</summary>
        public string Referrer { get; set; } = "";

        /// <summary>Página em que o formulário foi enviado.</summary>
        public string Page { get; set; } = "";

        // ------------------------------------------------ Andamento (tela do painel)
        public SiteContactStatus Status { get; set; } = SiteContactStatus.New;

        /// <summary>Anotações de quem atende (ex.: "retornar na segunda").</summary>
        public string Notes { get; set; } = "";

        public DateTime? StatusChangedAt { get; set; }
        public string StatusChangedBy { get; set; } = "";

        // ------------------------------------------------ Aviso por e-mail (serviço)
        /// <summary>Quando o aviso saiu. Nulo = ainda não saiu.</summary>
        public DateTime? NotifiedAt { get; set; }

        public int NotifyAttempts { get; set; }

        /// <summary>Motivo da última falha do aviso ("" = nenhuma).</summary>
        public string NotifyError { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Respostas fechadas do formulário do site e como aparecem para as pessoas.</summary>
    public static class SiteContactChoices
    {
        public static readonly IReadOnlyDictionary<string, string> Stores = new Dictionary<string, string>
        {
            ["1"] = "1 loja",
            ["2-5"] = "2 a 5 lojas",
            ["6-20"] = "6 a 20 lojas",
            ["21+"] = "Mais de 20 lojas",
        };

        public static readonly IReadOnlyDictionary<string, string> UsesUnifi = new Dictionary<string, string>
        {
            ["Yes"] = "Sim",
            ["No"] = "Não",
            ["Unknown"] = "Não sei",
        };

        public static readonly IReadOnlySet<string> States = new HashSet<string>
        {
            "AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT", "PA",
            "PB", "PE", "PI", "PR", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO",
        };

        /// <summary>Telefone para leitura: (91) 98888-7777 ou (91) 3222-1234.</summary>
        public static string FormatPhone(string sDigits) => sDigits.Length switch
        {
            11 => $"({sDigits[..2]}) {sDigits[2..7]}-{sDigits[7..]}",
            10 => $"({sDigits[..2]}) {sDigits[2..6]}-{sDigits[6..]}",
            _ => sDigits,
        };
    }
}
