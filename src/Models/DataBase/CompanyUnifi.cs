namespace Models.DataBase
{
    /// <summary>Como o back end alcança a controladora de uma unidade.</summary>
    public static class UnifiMode
    {
        /// <summary>Conexão direta com a controladora — exige IP público ou DDNS na unidade.</summary>
        public const string Local = "Local";

        /// <summary>
        /// Pela nuvem da Ubiquiti (Site Manager Connector Proxy). Para unidades sem IP público:
        /// falamos com api.ui.com e a Ubiquiti repassa o comando ao console pelo túnel que o
        /// próprio equipamento já mantém aberto.
        /// </summary>
        public const string Cloud = "Cloud";
    }

    public class CompanyUnifi
    {
        /// <summary>"Local" ou "Cloud" (ver <see cref="UnifiMode"/>). Padrão: Local.</summary>
        public string Mode { get; set; } = UnifiMode.Local;

        // ------------------------------------------------------------------ Modo Local
        public string Host { get; set; } = "";
        public string Site { get; set; } = "default";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public bool UnifiOs { get; set; } = true;
        public bool VerifySsl { get; set; }

        // ------------------------------------------------------------------ Modo Cloud
        /// <summary>
        /// Identificador do console na nuvem, como aparece na URL do unifi.ui.com
        /// (".../consoles/{ConsoleId}/network/...").
        /// </summary>
        public string ConsoleId { get; set; } = "";

        /// <summary>Chave do Site Manager (header X-API-KEY), guardada cifrada.</summary>
        public string ApiKey { get; set; } = "";

        /// <summary>
        /// UUID do site dentro do console. Descoberto sozinho na primeira chamada e guardado
        /// aqui para não repetir a consulta a cada visitante.
        /// </summary>
        public string SiteId { get; set; } = "";
    }
}
