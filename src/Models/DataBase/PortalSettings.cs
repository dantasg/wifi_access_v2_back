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

        /// <summary>
        /// URL para onde o visitante é redirecionado ao liberar o acesso (ex.: o Instagram da
        /// empresa). Nula/vazia = mantém o comportamento padrão (URL da UniFi / fallback).
        /// </summary>
        public string? RedirectUrl { get; set; }

        /// <summary>
        /// DDD do exemplo de telefone no portal ("(91) 90000-0000"). Vale para as unidades sem DDD
        /// próprio (<see cref="Unit.AreaCode"/>). Vazio = o exemplo mostra "(DDD)".
        /// </summary>
        public string AreaCode { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Paleta editável no admin — espelha ThemeColors de src/theme/theme.ts do front. Os padrões são
    /// neutros (branco, cinza e botão escuro): empresa nova sem tema salvo não parece marca de ninguém.
    /// Os mesmos valores estão em <c>neutralColors</c> no front.
    /// </summary>
    public class ThemeColors
    {
        public string Brand { get; set; } = "#4b5563";
        public string BrandDark { get; set; } = "#1f2937";
        public string Surface { get; set; } = "#f3f4f6";
        public string Card { get; set; } = "#ffffff";
        public string Field { get; set; } = "#f9fafb";
        public string Ink { get; set; } = "#111827";
        public string Muted { get; set; } = "#6b7280";
        public string Line { get; set; } = "#e5e7eb";
    }
}
