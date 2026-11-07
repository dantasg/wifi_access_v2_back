namespace Models.DataBase
{
    /// <summary>
    /// O número de WhatsApp da empresa na API oficial da Meta (PROPOSTA_WHATSAPP.md, D1): cada empresa usa a
    /// própria conta, como "desenvolvedor direto". Guardado dentro da empresa (Companies.WhatsApp_*), no mesmo
    /// padrão de <see cref="CompanyUnifi"/> na unidade.
    ///
    /// Os três segredos ficam cifrados (AES-GCM, "enc:v1:") e nunca vão para o front: a tela só recebe se
    /// existem (hasAccessToken, hasAppSecret, hasVerifyToken).
    /// </summary>
    public class CompanyWhatsApp
    {
        /// <summary>Id do número na Meta (Phone number ID), só dígitos.</summary>
        public string PhoneNumberId { get; set; } = "";

        /// <summary>Id da conta do WhatsApp na Meta (WABA ID), só dígitos. Usado para os modelos de mensagem.</summary>
        public string BusinessAccountId { get; set; } = "";

        /// <summary>Token de usuário do sistema, guardado cifrado.</summary>
        public string AccessToken { get; set; } = "";

        /// <summary>Chave secreta do app da Meta (confere a assinatura dos avisos), guardada cifrada.</summary>
        public string AppSecret { get; set; } = "";

        /// <summary>Código de verificação dos avisos, escolhido pelo admin, guardado cifrado.</summary>
        public string VerifyToken { get; set; } = "";

        // ------------------------------------------------------------------ Lido da Meta no último teste
        /// <summary>O número como a Meta mostra (ex.: "+55 91 98888-7777").</summary>
        public string DisplayPhoneNumber { get; set; } = "";

        /// <summary>O nome que aparece no WhatsApp do cliente (verified_name).</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>Qualidade do número na Meta: GREEN, YELLOW, RED ou UNKNOWN.</summary>
        public string QualityRating { get; set; } = "";

        /// <summary>Limite de envio (clientes diferentes em 24 h), como a Meta devolve: TIER_250, TIER_2K…</summary>
        public string MessagingLimit { get; set; } = "";

        /// <summary>Quando foi o último teste de conexão.</summary>
        public DateTime? CheckedAt { get; set; }

        /// <summary>O último teste passou. Só com ele o envio automático pode ser ligado.</summary>
        public bool CheckPassed { get; set; }

        /// <summary>Motivo da última falha, em português ("" = nenhuma).</summary>
        public string LastError { get; set; } = "";
    }
}
