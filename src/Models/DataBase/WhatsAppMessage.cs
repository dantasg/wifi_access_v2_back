namespace Models.DataBase
{
    /// <summary>
    /// Cada mensagem enviada pelo WhatsApp (PROPOSTA_WHATSAPP.md, §15.1): de onde veio, para quem, o texto exatamente
    /// como o cliente leu, o que foi pedido à Meta (sem o token) e o que ela respondeu, e a situação. O id da Meta
    /// (wamid) liga os avisos que chegam depois.
    ///
    /// Segue a regra do cliente (D10): é apagada junto com ele. A lista da campanha, que a retenção apaga antes, pode
    /// ir embora: a mensagem só perde o vínculo com o destinatário.
    /// </summary>
    public class WhatsAppMessage
    {
        public long Id { get; set; }
        public Guid IDCompany { get; set; }

        /// <summary>O cliente. Nulo = teste para um número informado no painel.</summary>
        public Guid? IDCustomer { get; set; }

        public Guid? IDCampaign { get; set; }
        public Guid? IDCampaignRun { get; set; }
        public long? IDCampaignRecipient { get; set; }

        /// <summary>Envio de teste pelo painel (não é de campanha). Username = quem mandou.</summary>
        public bool IsTest { get; set; }
        public string Username { get; set; } = "";

        /// <summary>Para quem: código do país + DDD + número, só dígitos (ex.: "5593991234567").</summary>
        public string ToPhone { get; set; } = "";

        /// <summary>O número do cliente no WhatsApp, como a Meta devolveu (wa_id).</summary>
        public string WaId { get; set; } = "";

        /// <summary>Por qual número da empresa saiu (Phone number ID e o número como aparece).</summary>
        public string FromPhoneNumberId { get; set; } = "";
        public string FromDisplayPhone { get; set; } = "";

        // ------------------------------------------------------------------ O texto
        public Guid? IDWhatsAppTemplate { get; set; }
        public string TemplateName { get; set; } = "";
        public string TemplateLanguage { get; set; } = "";

        /// <summary>A versão da campanha cujo texto saiu.</summary>
        public int? VersionNumber { get; set; }

        /// <summary>O valor de cada campo, na ordem ({{1}}, {{2}}…). JSON.</summary>
        public string ParametersJson { get; set; } = "[]";

        /// <summary>O texto exatamente como o cliente leu (corpo com os valores e o rodapé).</summary>
        public string Text { get; set; } = "";

        // ------------------------------------------------------------------ A conversa com a Meta
        public string? RequestJson { get; set; }
        public string? ResponseJson { get; set; }
        public int HttpStatus { get; set; }
        public long DurationMs { get; set; }

        /// <summary>O id da mensagem na Meta (wamid). "" = a Meta não aceitou.</summary>
        public string MetaMessageId { get; set; } = "";

        /// <summary>A situação de entrega que a Meta devolveu no envio (accepted, held_for_quality_assessment…).</summary>
        public string MetaMessageStatus { get; set; } = "";

        // ------------------------------------------------------------------ A situação
        /// <summary>Ver <see cref="WhatsAppMessageStatus"/>.</summary>
        public string Status { get; set; } = WhatsAppMessageStatus.Accepted;

        /// <summary>O código de erro da Meta e o motivo em português, quando falhou.</summary>
        public int? ErrorCode { get; set; }
        public string ErrorMessage { get; set; } = "";

        /// <summary>Cobrança, como vem nos avisos da Meta (parte 4).</summary>
        public string PricingCategory { get; set; } = "";
        public bool? Billable { get; set; }
        public string PricingType { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AcceptedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public DateTime? ReadAt { get; set; }
        public DateTime? FailedAt { get; set; }
    }

    public static class WhatsAppMessageStatus
    {
        /// <summary>A Meta aceitou o pedido; falta o aviso de envio.</summary>
        public const string Accepted = "Accepted";
        public const string Sent = "Sent";
        public const string Delivered = "Delivered";
        public const string Read = "Read";
        public const string Failed = "Failed";
    }
}
