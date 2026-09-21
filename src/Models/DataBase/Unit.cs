namespace Models.DataBase
{
    /// <summary>
    /// Unidade (franquia/loja) de uma empresa. Tema/login/relatório continuam por empresa;
    /// a controladora UniFi e os leads passam a ser por unidade.
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
        public bool Active { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Controladora UniFi desta unidade.</summary>
        public CompanyUnifi Unifi { get; set; } = new CompanyUnifi();
    }
}
