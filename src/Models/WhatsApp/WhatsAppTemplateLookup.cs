using Models.DataBase;

namespace Models.WhatsApp
{
    /// <summary>
    /// O modelo da Meta de uma campanha. Fallback = o último texto aprovado de uma versão anterior, que é o que
    /// sai enquanto o texto atual não está aprovado (PROPOSTA_WHATSAPP.md, §11.2).
    /// </summary>
    public sealed record WhatsAppCampaignTemplate(
        WhatsAppTemplate? Current, WhatsAppTemplate? Fallback, int? FallbackVersion);

    /// <summary>Acha o modelo de cada versão de campanha pelo texto (o mesmo corpo e rodapé montados pelo builder).</summary>
    public static class WhatsAppTemplateLookup
    {
        /// <summary>
        /// O modelo com o mesmo texto. Se houver mais de um (um desativado na Meta e o novo que o substituiu), vale
        /// o que ainda pode ser usado, e entre eles o mais novo.
        /// </summary>
        public static WhatsAppTemplate? Match(WhatsAppTemplateContent objContent, IEnumerable<WhatsAppTemplate> objTemplates) =>
            objTemplates
                .Where(template => template.BodyText == objContent.BodyText && template.FooterText == objContent.FooterText)
                .OrderBy(template => template.Status == WhatsAppTemplateStatus.Disabled ? 1 : 0)
                .ThenByDescending(template => template.CreatedAt)
                .FirstOrDefault();

        /// <param name="objVersions">As versões da campanha, da mais nova (a atual) para a mais antiga: número e texto.</param>
        public static WhatsAppCampaignTemplate ForCampaign(
            string sCompanyName,
            IReadOnlyList<(int iNumber, string sMessage)> objVersions,
            IReadOnlyList<WhatsAppTemplate> objTemplates)
        {
            if (objVersions.Count == 0)
            {
                return new WhatsAppCampaignTemplate(null, null, null);
            }

            WhatsAppTemplate? objCurrent = Match(
                WhatsAppTemplateBuilder.Build(objVersions[0].sMessage, sCompanyName), objTemplates);
            if (objCurrent?.Status == WhatsAppTemplateStatus.Approved)
            {
                return new WhatsAppCampaignTemplate(objCurrent, null, null);
            }

            foreach ((int iNumber, string sMessage) in objVersions.Skip(1))
            {
                WhatsAppTemplate? objOlder = Match(WhatsAppTemplateBuilder.Build(sMessage, sCompanyName), objTemplates);
                if (objOlder?.Status == WhatsAppTemplateStatus.Approved)
                {
                    return new WhatsAppCampaignTemplate(objCurrent, objOlder, iNumber);
                }
            }
            return new WhatsAppCampaignTemplate(objCurrent, null, null);
        }

        /// <summary>
        /// Aplica a situação que a Meta devolveu (na consulta ou no aviso): a nossa situação, o motivo em português e a
        /// categoria. Devolve true se algo mudou. Usado pelo serviço e pelos avisos da Meta.
        /// </summary>
        public static bool ApplyMetaStatus(
            WhatsAppTemplate objTemplate, string sMetaStatus, string sCategory, string sRejectedReason, DateTime dtNowUtc)
        {
            string sStatus = WhatsAppTemplateStatus.FromMeta(sMetaStatus);
            string sReason = sStatus switch
            {
                WhatsAppTemplateStatus.Rejected => sRejectedReason.Length > 0
                    ? RejectedReason(sRejectedReason)
                    : objTemplate.Reason,
                WhatsAppTemplateStatus.Paused =>
                    "A Meta pausou o modelo porque muitos clientes bloquearam ou denunciaram a mensagem.",
                WhatsAppTemplateStatus.Disabled => $"A Meta desativou o modelo ({sMetaStatus}).",
                _ => "",
            };
            string sNewCategory = sCategory.Length > 0 ? sCategory : objTemplate.Category;

            bool bChanged = sStatus != objTemplate.Status || sMetaStatus != objTemplate.MetaStatus
                || sReason != objTemplate.Reason || sNewCategory != objTemplate.Category;
            if (bChanged)
            {
                objTemplate.Status = sStatus;
                objTemplate.MetaStatus = Truncate(sMetaStatus, 30);
                objTemplate.Reason = Truncate(sReason, 1000);
                objTemplate.Category = Truncate(sNewCategory, 30);
                objTemplate.StatusChangedAt = dtNowUtc;
            }
            return bChanged;
        }

        /// <summary>Texto recusado, pausado ou desativado: o super admin precisa saber (D9).</summary>
        public static bool NeedsAlert(string sStatus) =>
            sStatus is WhatsAppTemplateStatus.Rejected or WhatsAppTemplateStatus.Paused or WhatsAppTemplateStatus.Disabled;

        private static string Truncate(string sValue, int iMax) => sValue.Length <= iMax ? sValue : sValue[..iMax];

        /// <summary>A situação em português, para o registro da conta.</summary>
        public static string StatusLabel(string sStatus) => sStatus switch
        {
            WhatsAppTemplateStatus.Queued => "na fila",
            WhatsAppTemplateStatus.Invalid => "texto precisa de ajuste",
            WhatsAppTemplateStatus.Error => "erro ao enviar",
            WhatsAppTemplateStatus.Pending => "em análise",
            WhatsAppTemplateStatus.Approved => "aprovado",
            WhatsAppTemplateStatus.Rejected => "recusado",
            WhatsAppTemplateStatus.Paused => "pausado pela Meta",
            WhatsAppTemplateStatus.Disabled => "desativado",
            _ => sStatus,
        };

        /// <summary>O motivo da recusa da Meta (rejected_reason) em português.</summary>
        public static string RejectedReason(string sMetaReason) => sMetaReason.ToUpperInvariant() switch
        {
            "" or "NONE" => "A Meta recusou o texto sem dizer o motivo. Revise o texto e salve de novo.",
            "ABUSIVE_CONTENT" or "SCAM" or "PROMOTIONAL" =>
                "A Meta achou que o texto não segue as regras dela de conteúdo. Revise o texto e salve de novo.",
            "INCORRECT_CATEGORY" or "TAG_CONTENT_MISMATCH" =>
                "A Meta achou que o texto não combina com a categoria marketing.",
            "INVALID_FORMAT" =>
                "A Meta achou o formato inválido (campos, tamanho ou caracteres). Revise o texto e salve de novo.",
            _ => $"A Meta recusou o texto ({sMetaReason}).",
        };
    }
}
