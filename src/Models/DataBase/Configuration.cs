namespace Models.DataBase
{
    /// <summary>
    /// Configurações globais do sistema em formato chave-valor (ambos string).
    /// A chave (<see cref="IDConfiguration"/>) identifica o parâmetro, ex.: "SMTP_HOST".
    /// Usada para não guardar segredos (SMTP etc.) no appsettings.json.
    /// </summary>
    public class Configuration
    {
        /// <summary>Chave do parâmetro (PK). Ex.: "SMTP_HOST", "SMTP_PASSWORD".</summary>
        public string IDConfiguration { get; set; } = "";

        /// <summary>Valor do parâmetro, como string.</summary>
        public string Value { get; set; } = "";
    }
}
