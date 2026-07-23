namespace Models.DataBase
{
    /// <summary>
    /// Configurações do portal (tema + parâmetros de acesso). Uma linha por empresa
    /// (IDCompany é único); sem linha, valem os padrões da marca.
    /// </summary>
    public class PortalSettings
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Empresa dona destas configurações (multi tenant, uma linha por empresa).</summary>
        public Guid IDCompany { get; set; }

        public ThemeColors Colors { get; set; } = new ThemeColors();

        /// <summary>Imagens como data URL (o front envia assim); null = usar o padrão da marca.</summary>
        public string? Logo { get; set; }
        public string? Favicon { get; set; }
        public string? Banner { get; set; }

        public string Ssid { get; set; } = string.Empty;

        /// <summary>Tempo de liberação do guest, em minutos.</summary>
        public int AccessMinutes { get; set; } = 1440;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Paleta editável no admin — espelha ThemeColors de src/theme/theme.ts do front.</summary>
    public class ThemeColors
    {
        public string Brand { get; set; } = "#c8a46d";
        public string BrandDark { get; set; } = "#8a6d3c";
        public string Surface { get; set; } = "#f3ebdd";
        public string Card { get; set; } = "#fffdf8";
        public string Field { get; set; } = "#fbf7ef";
        public string Ink { get; set; } = "#3a3128";
        public string Muted { get; set; } = "#9a8c78";
        public string Line { get; set; } = "#e7ddcc";
    }
}
