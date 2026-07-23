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
        public bool Active { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Controladora UniFi desta unidade.</summary>
        public CompanyUnifi Unifi { get; set; } = new CompanyUnifi();
    }
}
