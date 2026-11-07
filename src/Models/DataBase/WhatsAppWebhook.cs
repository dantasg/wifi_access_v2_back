namespace Models.DataBase
{
    /// <summary>
    /// Cada aviso que chega da Meta, do jeito que chegou (PROPOSTA_WHATSAPP.md, §15.1 e D12): a prova do que a Meta
    /// mandou, para conferir ou tratar de novo. Um aviso pode misturar vários clientes, por isso fica só 90 dias; os
    /// pedaços de cada cliente seguem a regra do cliente.
    /// </summary>
    public class WhatsAppWebhookRequest
    {
        public long Id { get; set; }

        /// <summary>A empresa do endereço (webhooks/whatsapp/{empresa}).</summary>
        public Guid IDCompany { get; set; }

        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

        /// <summary>A assinatura (X-Hub-Signature-256) conferiu com a chave secreta do app da empresa.</summary>
        public bool SignatureValid { get; set; }

        /// <summary>O corpo como chegou (sem assinatura válida, só o começo).</summary>
        public string Body { get; set; } = "";

        /// <summary>Os campos do aviso ("messages", "message_template_status_update"…), separados por vírgula.</summary>
        public string Fields { get; set; } = "";

        /// <summary>Quando foi tratado. Nulo = ainda não (ou falhou).</summary>
        public DateTime? ProcessedAt { get; set; }

        /// <summary>O que deu errado ao tratar ("" = nada).</summary>
        public string Error { get; set; } = "";

        public string RemoteIp { get; set; } = "";
    }

    /// <summary>
    /// Cada mudança de situação de uma mensagem enviada (enviada, entregue, lida, falhou), como a Meta avisou: a hora
    /// que ela informou, o erro, a cobrança e o pedaço do aviso.
    /// </summary>
    public class WhatsAppMessageEvent
    {
        public long Id { get; set; }
        public long IDWhatsAppMessage { get; set; }

        /// <summary>Ver <see cref="WhatsAppMessageStatus"/>.</summary>
        public string Status { get; set; } = "";

        /// <summary>A hora informada pela Meta (UTC).</summary>
        public DateTime? MetaTimestamp { get; set; }

        public int? ErrorCode { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string PricingCategory { get; set; } = "";
        public string PricingType { get; set; } = "";
        public bool? Billable { get; set; }

        /// <summary>O pedaço do aviso desta mensagem, como chegou. JSON.</summary>
        public string PayloadJson { get; set; } = "{}";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Cada mensagem de texto de um cliente cadastrado (D11: só texto; D13: só clientes), com a campanha a que ela
    /// pertence (D14: a da última mensagem de campanha enviada a ele antes da resposta) e se era um "SAIR" (D7).
    /// Segue a regra do cliente (D10): é apagada junto com ele.
    /// </summary>
    public class WhatsAppInboundMessage
    {
        public long Id { get; set; }
        public Guid IDCompany { get; set; }
        public Guid IDCustomer { get; set; }

        /// <summary>O id da Meta: a mesma mensagem não é gravada duas vezes (a Meta repete avisos).</summary>
        public string MetaMessageId { get; set; } = "";

        /// <summary>O número do cliente no WhatsApp (wa_id) e o nome do perfil dele.</summary>
        public string FromPhone { get; set; } = "";
        public string ProfileName { get; set; } = "";

        /// <summary>O texto exatamente como chegou.</summary>
        public string Text { get; set; } = "";

        /// <summary>Era um "SAIR": o cliente saiu do envio automático (D7).</summary>
        public bool IsOptOut { get; set; }

        /// <summary>A campanha a que a resposta pertence (D14). Nulo = nenhuma mensagem de campanha antes.</summary>
        public Guid? IDCampaign { get; set; }
        public Guid? IDCampaignRun { get; set; }

        /// <summary>A mensagem de campanha que ela responde.</summary>
        public long? IDWhatsAppMessage { get; set; }

        /// <summary>O id da mensagem citada, quando o cliente respondeu citando (context.id).</summary>
        public string ContextMessageId { get; set; } = "";

        /// <summary>A hora informada pela Meta (UTC).</summary>
        public DateTime SentAt { get; set; }

        /// <summary>O pedaço do aviso desta mensagem, como chegou. JSON.</summary>
        public string PayloadJson { get; set; } = "{}";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Alerta por e-mail aos responsáveis pelo sistema (D9): token inválido, texto recusado ou pausado, WhatsApp que
    /// não pôde ser usado numa campanha, qualidade baixa. Quem cria é a API ou o serviço; quem manda é o serviço.
    /// O mesmo assunto (Key) não se repete em 12 horas.
    /// </summary>
    public class WhatsAppAlert
    {
        public long Id { get; set; }
        public Guid IDCompany { get; set; }

        /// <summary>O assunto do alerta para não repetir (ex.: "token", "template:aw_birthday_x_v1").</summary>
        public string Key { get; set; } = "";

        public string Subject { get; set; } = "";
        public string Body { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Quando saiu. Nulo = na fila.</summary>
        public DateTime? SentAt { get; set; }

        public int Attempts { get; set; }

        /// <summary>Por que não saiu ("" = nada; "sem e-mail de alertas" quando não há para quem mandar).</summary>
        public string Error { get; set; } = "";
    }
}
