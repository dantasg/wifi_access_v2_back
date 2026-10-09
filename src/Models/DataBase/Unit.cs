namespace Models.DataBase
{
    /// <summary>
    /// Unidade (franquia/loja) de uma empresa. Tema e login continuam por empresa; a controladora
    /// UniFi, os leads e o e-mail (relatório mensal e campanhas) são por unidade.
    /// </summary>
    public class Unit
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid IDCompany { get; set; }
        public string Name { get; set; } = "";

        /// <summary>Identifica a unidade na URL do portal (?unit=slug). Único globalmente.</summary>
        public string Slug { get; set; } = "";

        /// <summary>
        /// Endereço (FQDN) em que o portal desta unidade é aberto — ex.: "itaituba.wifi.exemplo.com.br".
        /// A UniFi só aceita um host no campo de portal externo, sem query string, então é por aqui
        /// que o portal descobre de qual unidade ele é quando não vem "?unit=". Vazio = não usa.
        /// </summary>
        public string PortalHost { get; set; } = "";

        /// <summary>
        /// Para onde o visitante desta unidade vai depois de liberado (ex.: o Instagram da loja).
        /// Vazio = usa a URL "Geral" da empresa (<see cref="PortalSettings.RedirectUrl"/>).
        /// </summary>
        public string RedirectUrl { get; set; } = "";

        /// <summary>
        /// E-mail da unidade (do gerente): recebe o relatório mensal com os cadastros desta unidade e o
        /// PDF de cada campanha com os clientes dela. Vazio = não recebe nada.
        /// </summary>
        public string Email { get; set; } = "";

        /// <summary>
        /// DDD da loja no exemplo de telefone do portal, para lojas da mesma empresa em estados diferentes.
        /// Vazio = usa o da empresa (<see cref="PortalSettings.Ddd"/>).
        /// </summary>
        public string Ddd { get; set; } = "";

        /// <summary>
        /// Quando o último relatório mensal desta unidade foi enviado (UTC). Marcador de idempotência:
        /// o serviço não reenvia no mesmo mês. Nulo = nunca enviado.
        /// </summary>
        public DateTime? LastReportSentAt { get; set; }

        /// <summary>
        /// Última leitura dos aparelhos desta unidade na nuvem da UniFi (UTC). Nulo = ainda não leu: enquanto
        /// isso, a unidade continua sendo achada pelo endereço do portal, como antes.
        /// </summary>
        public DateTime? DevicesSyncedAt { get; set; }

        /// <summary>Motivo da última leitura que falhou ("" = deu certo). Os aparelhos já lidos continuam valendo.</summary>
        public string DevicesSyncError { get; set; } = "";

        public bool Active { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Controladora UniFi desta unidade.</summary>
        public CompanyUnifi Unifi { get; set; } = new CompanyUnifi();
    }
}
