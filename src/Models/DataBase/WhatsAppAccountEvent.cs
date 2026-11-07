namespace Models.DataBase
{
    /// <summary>
    /// Registro de tudo o que acontece com a conta de WhatsApp de uma empresa (PROPOSTA_WHATSAPP.md, §15):
    /// credenciais trocadas, testes de conexão, envio automático ligado ou desligado, WhatsApp das lojas e
    /// modelos de mensagem. Sem dado pessoal e
    /// sem segredo nenhum, por isso não expira.
    /// </summary>
    public class WhatsAppAccountEvent
    {
        public long Id { get; set; }
        public Guid IDCompany { get; set; }

        /// <summary>Ver <see cref="WhatsAppAccountEventKind"/>.</summary>
        public string Kind { get; set; } = "";

        /// <summary>Quem fez ("" = o próprio sistema).</summary>
        public string Username { get; set; } = "";

        /// <summary>O que aconteceu, em português (ex.: "Trocou: token, id do número").</summary>
        public string Details { get; set; } = "";

        /// <summary>O que foi pedido à Meta, sem o token (só nos testes e chamadas à Meta).</summary>
        public string? RequestJson { get; set; }

        /// <summary>O que a Meta respondeu.</summary>
        public string? ResponseJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public static class WhatsAppAccountEventKind
    {
        public const string CredentialsChanged = "CredentialsChanged";
        public const string ConnectionTested = "ConnectionTested";
        public const string Enabled = "Enabled";
        public const string Disabled = "Disabled";

        /// <summary>O WhatsApp de atendimento de alguma loja mudou (vai no fim das mensagens, D10).</summary>
        public const string StorePhonesChanged = "StorePhonesChanged";

        /// <summary>Um modelo de mensagem foi mandado para a Meta (ou a ida falhou).</summary>
        public const string TemplateSubmitted = "TemplateSubmitted";

        /// <summary>A Meta mudou a situação de um modelo (aprovou, recusou, pausou…).</summary>
        public const string TemplateStatusChanged = "TemplateStatusChanged";

        /// <summary>A Meta avisou mudança no número (limite de envios, qualidade).</summary>
        public const string PhoneUpdated = "PhoneUpdated";

        /// <summary>A Meta conferiu o endereço dos avisos (código de verificação certo).</summary>
        public const string WebhookVerified = "WebhookVerified";

        /// <summary>A Meta tentou conferir o endereço dos avisos com o código de verificação errado.</summary>
        public const string WebhookVerifyFailed = "WebhookVerifyFailed";
    }
}
