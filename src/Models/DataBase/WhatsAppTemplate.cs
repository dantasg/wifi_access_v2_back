namespace Models.DataBase
{
    /// <summary>
    /// Um modelo de mensagem da Meta (PROPOSTA_WHATSAPP.md, parte 2, D8): o texto de uma campanha já no formato
    /// do WhatsApp. Cada texto diferente vira um modelo; versões da campanha com o mesmo texto usam o mesmo.
    /// Quem cria e manda para a Meta é o serviço; a tela só lê a situação.
    ///
    /// A ligação com a campanha é pelo texto (<see cref="BodyText"/>): o modelo da versão atual é o que tem o
    /// mesmo corpo, montado por <see cref="WhatsApp.WhatsAppTemplateBuilder"/>.
    /// </summary>
    public class WhatsAppTemplate
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid IDCompany { get; set; }

        /// <summary>A campanha e a versão que criaram o modelo (outra campanha com o mesmo texto usa o mesmo).</summary>
        public Guid IDCampaign { get; set; }
        public int VersionNumber { get; set; }

        /// <summary>Nome na Meta: só minúsculas, números e "_" (ex.: "aw_birthday_3f2a1b9c_v4").</summary>
        public string Name { get; set; } = "";

        /// <summary>Idioma na Meta: "pt_BR".</summary>
        public string Language { get; set; } = "pt_BR";

        /// <summary>Categoria pedida ("MARKETING"); a Meta pode trocar, e aqui fica a que ela devolveu.</summary>
        public string Category { get; set; } = "MARKETING";

        /// <summary>O corpo exatamente como foi para a Meta, com {{1}}, {{2}}…</summary>
        public string BodyText { get; set; } = "";

        /// <summary>O rodapé ("Para não receber mais, responda SAIR.").</summary>
        public string FooterText { get; set; } = "";

        /// <summary>
        /// O campo de cada variável, na ordem: ["primeiro_nome", "telefone_loja"] = {{1}} é o primeiro nome e
        /// {{2}} o WhatsApp da loja. JSON.
        /// </summary>
        public string ParametersJson { get; set; } = "[]";

        /// <summary>Ver <see cref="WhatsAppTemplateStatus"/>.</summary>
        public string Status { get; set; } = WhatsAppTemplateStatus.Queued;

        /// <summary>A situação como a Meta devolveu (PENDING, APPROVED, REJECTED, PAUSED…). "" = ainda não foi.</summary>
        public string MetaStatus { get; set; } = "";

        /// <summary>Id do modelo na Meta. "" = ainda não foi criado lá.</summary>
        public string MetaTemplateId { get; set; } = "";

        /// <summary>Por que não pode ser usado, em português: recusa da Meta ou texto que não segue as regras.</summary>
        public string Reason { get; set; } = "";

        /// <summary>Última falha ao falar com a Meta (token, rede…), em português. "" = nenhuma.</summary>
        public string LastError { get; set; } = "";

        /// <summary>Tentativas de criar o modelo na Meta.</summary>
        public int Attempts { get; set; }

        /// <summary>Depois de uma falha, quando tentar de novo (UTC).</summary>
        public DateTime? NextAttemptAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Quando a Meta aceitou o pedido de criação.</summary>
        public DateTime? SubmittedAt { get; set; }

        /// <summary>Quando a situação mudou pela última vez.</summary>
        public DateTime? StatusChangedAt { get; set; }

        /// <summary>Quando a situação foi conferida na Meta pela última vez.</summary>
        public DateTime? CheckedAt { get; set; }
    }

    public static class WhatsAppTemplateStatus
    {
        /// <summary>Criado aqui, esperando o serviço mandar para a Meta.</summary>
        public const string Queued = "Queued";

        /// <summary>O texto não segue as regras do WhatsApp (ex.: grande demais). Não vai para a Meta.</summary>
        public const string Invalid = "Invalid";

        /// <summary>A Meta não recebeu o pedido (token, rede…). O serviço tenta de novo.</summary>
        public const string Error = "Error";

        /// <summary>Em análise na Meta (até 24 h).</summary>
        public const string Pending = "Pending";

        public const string Approved = "Approved";
        public const string Rejected = "Rejected";

        /// <summary>Pausado pela Meta (muitos bloqueios ou denúncias). Volta sozinho ou com edição.</summary>
        public const string Paused = "Paused";

        /// <summary>Desativado, arquivado ou apagado na Meta. Não pode ser usado.</summary>
        public const string Disabled = "Disabled";

        /// <summary>A situação da Meta traduzida para a nossa.</summary>
        public static string FromMeta(string sMetaStatus) => sMetaStatus.ToUpperInvariant() switch
        {
            "APPROVED" => Approved,
            "PENDING" or "IN_APPEAL" => Pending,
            "REJECTED" => Rejected,
            "PAUSED" => Paused,
            _ => Disabled,
        };
    }
}
