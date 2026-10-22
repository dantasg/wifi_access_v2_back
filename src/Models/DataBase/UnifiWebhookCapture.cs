namespace Models.DataBase
{
    /// <summary>
    /// TEMPORÁRIA (22/10/2026): tudo o que o Alarm Manager da UniFi mandar para /webhooks/unifi, guardado como
    /// chegou, para analisarmos juntos o que vem no aviso de "cliente conectou" (se traz o MAC do aparelho, a
    /// loja etc.) antes de decidir o desenho do registro de visitas sem portal. Apagar a tabela depois da análise.
    /// </summary>
    public class UnifiWebhookCapture
    {
        public long Id { get; set; }
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

        /// <summary>GET ou POST (o Alarm Manager deixa escolher).</summary>
        public string Method { get; set; } = "";

        /// <summary>O "?unit=" do endereço cadastrado na UniFi, para saber de qual loja veio ("" = sem).</summary>
        public string Label { get; set; } = "";

        /// <summary>A query string inteira, como chegou (o código secreto fica no caminho, não aqui).</summary>
        public string QueryString { get; set; } = "";

        /// <summary>Os cabeçalhos do pedido, em JSON.</summary>
        public string HeadersJson { get; set; } = "{}";

        public string ContentType { get; set; } = "";

        /// <summary>O corpo exatamente como chegou (até 64 KB).</summary>
        public string Body { get; set; } = "";

        /// <summary>O corpo como JSON, quando for JSON válido (para consultar no banco).</summary>
        public string? BodyJson { get; set; }

        public string RemoteIp { get; set; } = "";
    }
}
