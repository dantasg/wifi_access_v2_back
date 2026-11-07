using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models.Campaigns;
using Models.DataBase;
using Models.Persistence;
using Models.Security;
using Models.WhatsApp;

namespace AccessWifiService.WhatsApp
{
    public sealed class WhatsAppTemplateOptions
    {
        public const string SectionName = "WhatsAppTemplates";

        /// <summary>Intervalo entre as voltas do serviço.</summary>
        public int TickSeconds { get; set; } = 60;

        /// <summary>De quanto em quanto tempo conferir na Meta um modelo em análise (caso o aviso se perca).</summary>
        public int PendingCheckMinutes { get; set; } = 15;

        /// <summary>De quanto em quanto tempo conferir um modelo aprovado ou pausado (a Meta pode pausar ou desativar).</summary>
        public int ApprovedCheckHours { get; set; } = 6;

        /// <summary>Quantos modelos mandar para a Meta por volta, por empresa (a Meta aceita até 100 por hora).</summary>
        public int MaxSubmitsPerTick { get; set; } = 10;

        /// <summary>Quantos modelos conferir por volta, por empresa.</summary>
        public int MaxChecksPerTick { get; set; } = 20;

        /// <summary>De quanto em quanto tempo conferir o número na Meta (qualidade, limite e se o token ainda vale).</summary>
        public int PhoneCheckHours { get; set; } = 6;
    }

    /// <summary>
    /// Modelos de mensagem da Meta (PROPOSTA_WHATSAPP.md, parte 2, D8), em cada empresa com a conexão testada:
    /// <list type="number">
    /// <item>cria o modelo do texto atual de cada campanha que ainda não tem um;</item>
    /// <item>manda para a Meta os que estão na fila (e tenta de novo os que falharam, com espera crescente);</item>
    /// <item>confere a situação dos que estão em análise, aprovados ou pausados, caso o aviso da Meta se perca.</item>
    /// </list>
    /// Tudo vai para o registro da conta (WhatsAppAccountEvents), com o que foi pedido e o que a Meta respondeu,
    /// sem o token.
    /// </summary>
    public sealed class WhatsAppTemplateService
    {
        private readonly AppDbContext _objDbContext;
        private readonly IEncryptor _objEncryptor;
        private readonly WhatsAppCloudClient _objClient;
        private readonly WhatsAppTemplateOptions _objOptions;
        private readonly ILogger<WhatsAppTemplateService> _objLogger;

        public WhatsAppTemplateService(
            AppDbContext objDbContext,
            IEncryptor objEncryptor,
            WhatsAppCloudClient objClient,
            WhatsAppTemplateOptions objOptions,
            ILogger<WhatsAppTemplateService> objLogger)
        {
            _objDbContext = objDbContext;
            _objEncryptor = objEncryptor;
            _objClient = objClient;
            _objOptions = objOptions;
            _objLogger = objLogger;
        }

        public async Task TickAsync(DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            // Só empresas com a conexão testada: sem isso, a Meta recusaria tudo.
            List<Company> objCompanies = await _objDbContext.Companies
                .Where(company => company.WhatsApp.CheckPassed
                    && company.WhatsApp.BusinessAccountId != ""
                    && company.WhatsApp.AccessToken != "")
                .ToListAsync(objCancellationToken);

            foreach (Company objCompany in objCompanies)
            {
                try
                {
                    await SyncCompanyAsync(objCompany, dtNowUtc, objCancellationToken);
                }
                catch (Exception objException) when (objException is not OperationCanceledException)
                {
                    _objLogger.LogError(objException, "WhatsApp: falha nos modelos da empresa {Slug}.", objCompany.Slug);
                }
            }
        }

        private async Task SyncCompanyAsync(Company objCompany, DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            string sAccessToken = _objEncryptor.Decrypt(objCompany.WhatsApp.AccessToken) ?? "";
            if (sAccessToken.Length == 0)
            {
                return;
            }

            List<WhatsAppTemplate> objTemplates = await _objDbContext.WhatsAppTemplates
                .Where(template => template.IDCompany == objCompany.Id)
                .ToListAsync(objCancellationToken);
            Dictionary<Guid, Campaign> objCampaigns = await _objDbContext.Campaigns.AsNoTracking()
                .Where(campaign => campaign.IDCompany == objCompany.Id)
                .ToDictionaryAsync(campaign => campaign.Id, objCancellationToken);

            if (!await CheckPhoneAsync(objCompany, sAccessToken, dtNowUtc, objCancellationToken))
            {
                return;
            }
            await QueueMissingAsync(objCompany, objCampaigns.Values, objTemplates, dtNowUtc, objCancellationToken);
            await SubmitQueuedAsync(objCompany, sAccessToken, objTemplates, objCampaigns, dtNowUtc, objCancellationToken);
            await RefreshStatusesAsync(objCompany, sAccessToken, objTemplates, dtNowUtc, objCancellationToken);
        }

        /// <summary>
        /// Campanha ativa ou pausada cujo texto atual ainda não tem modelo: cria na fila. Texto que não segue as
        /// regras do WhatsApp fica como "precisa de ajuste" e não vai para a Meta.
        /// </summary>
        private async Task QueueMissingAsync(
            Company objCompany, IEnumerable<Campaign> objCampaigns, List<WhatsAppTemplate> objTemplates,
            DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            bool bAdded = false;
            foreach (Campaign objCampaign in objCampaigns
                .Where(campaign => campaign.Status != CampaignStatus.Finished)
                .OrderBy(campaign => campaign.CreatedAt))
            {
                CampaignConfig objConfig = CampaignConfig.FromJson(objCampaign.ConfigJson);
                if (objConfig.Channel != CampaignChannel.WhatsApp)
                {
                    continue;
                }

                WhatsAppTemplateContent objContent = WhatsAppTemplateBuilder.Build(objConfig.Message, objCompany.Name);
                WhatsAppTemplate? objExisting = WhatsAppTemplateLookup.Match(objContent, objTemplates);
                // Desativado ou apagado na Meta: o mesmo texto ganha um modelo novo, com outro nome.
                if (objExisting is not null && objExisting.Status != WhatsAppTemplateStatus.Disabled)
                {
                    continue;
                }

                WhatsAppTemplate objTemplate = new WhatsAppTemplate
                {
                    IDCompany = objCompany.Id,
                    IDCampaign = objCampaign.Id,
                    VersionNumber = objCampaign.CurrentVersion,
                    Name = UniqueName(
                        WhatsAppTemplateBuilder.NameFor(objCampaign.Kind, objCampaign.Id, objCampaign.CurrentVersion),
                        objTemplates),
                    Language = WhatsAppTemplateBuilder.Language,
                    Category = WhatsAppTemplateBuilder.Category,
                    BodyText = objContent.BodyText,
                    FooterText = objContent.FooterText,
                    ParametersJson = WhatsAppTemplateBuilder.ParametersToJson(objContent.Parameters),
                    Status = objContent.Invalid is null ? WhatsAppTemplateStatus.Queued : WhatsAppTemplateStatus.Invalid,
                    Reason = objContent.Invalid ?? "",
                    CreatedAt = dtNowUtc,
                    StatusChangedAt = dtNowUtc,
                };
                _objDbContext.WhatsAppTemplates.Add(objTemplate);
                objTemplates.Add(objTemplate);
                bAdded = true;
            }

            if (bAdded)
            {
                await _objDbContext.SaveChangesAsync(objCancellationToken);
            }
        }

        private async Task SubmitQueuedAsync(
            Company objCompany, string sAccessToken, List<WhatsAppTemplate> objTemplates,
            IReadOnlyDictionary<Guid, Campaign> objCampaigns, DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            List<WhatsAppTemplate> objDue = objTemplates
                .Where(template => template.Status == WhatsAppTemplateStatus.Queued
                    || (template.Status == WhatsAppTemplateStatus.Error && template.NextAttemptAt <= dtNowUtc))
                .OrderBy(template => template.CreatedAt)
                .Take(Math.Max(1, _objOptions.MaxSubmitsPerTick))
                .ToList();
            if (objDue.Count == 0)
            {
                return;
            }

            WhatsAppTemplateSamples objSamples =
                await WhatsAppTemplateSamples.LoadAsync(_objDbContext, objCompany.Id, objCancellationToken);
            foreach (WhatsAppTemplate objTemplate in objDue)
            {
                string sCampaign = objCampaigns.TryGetValue(objTemplate.IDCampaign, out Campaign? objCampaign)
                    ? $"\"{objCampaign.Name}\" v{objTemplate.VersionNumber}"
                    : $"v{objTemplate.VersionNumber}";
                List<string> objParameters = WhatsAppTemplateBuilder.ParametersFromJson(objTemplate.ParametersJson);
                objTemplate.Attempts++;
                try
                {
                    (WhatsAppTemplateInfo objInfo, List<WhatsAppCall> objCalls) = await _objClient.CreateTemplateAsync(
                        objCompany.WhatsApp.BusinessAccountId, sAccessToken, objTemplate.Name, objTemplate.Language,
                        objTemplate.Category, objTemplate.BodyText,
                        WhatsAppTemplateBuilder.Examples(objParameters, objSamples), objTemplate.FooterText,
                        objCancellationToken);
                    Accept(objTemplate, objInfo, dtNowUtc);
                    AddEvent(objCompany, WhatsAppAccountEventKind.TemplateSubmitted,
                        $"Modelo {objTemplate.Name} (campanha {sCampaign}) enviado para a Meta: "
                        + $"{WhatsAppTemplateLookup.StatusLabel(objTemplate.Status)}.", dtNowUtc, objCalls);
                }
                catch (WhatsAppApiException objException)
                    when (objException.MetaSubcode == WhatsAppCloudClient.DuplicateTemplateSubcode)
                {
                    // O nome já existe na Meta (ex.: o serviço caiu depois de criar e antes de gravar): adota o que está lá.
                    List<WhatsAppCall> objCalls = [.. objException.Calls];
                    WhatsAppTemplateInfo? objFound = null;
                    try
                    {
                        (objFound, List<WhatsAppCall> objFindCalls) = await _objClient.FindTemplateAsync(
                            objCompany.WhatsApp.BusinessAccountId, sAccessToken, objTemplate.Name, objTemplate.Language,
                            objCancellationToken);
                        objCalls.AddRange(objFindCalls);
                    }
                    catch (WhatsAppApiException objFindException)
                    {
                        objCalls.AddRange(objFindException.Calls);
                    }

                    if (objFound is not null)
                    {
                        Accept(objTemplate, objFound, dtNowUtc);
                        AddEvent(objCompany, WhatsAppAccountEventKind.TemplateSubmitted,
                            $"Modelo {objTemplate.Name} (campanha {sCampaign}) já existia na Meta e foi adotado: "
                            + $"{WhatsAppTemplateLookup.StatusLabel(objTemplate.Status)}.", dtNowUtc, objCalls);
                    }
                    else
                    {
                        Fail(objTemplate, objException.Message, dtNowUtc);
                        AddEvent(objCompany, WhatsAppAccountEventKind.TemplateSubmitted,
                            $"Modelo {objTemplate.Name} (campanha {sCampaign}): falhou. {objException.Message}", dtNowUtc, objCalls);
                    }
                }
                catch (WhatsAppApiException objException)
                {
                    bool bTextProblem = objException.MetaCode == 100;
                    if (bTextProblem)
                    {
                        // Parâmetro inválido no modelo: tentar de novo daria o mesmo. Fica para ajustar o texto.
                        objTemplate.Status = WhatsAppTemplateStatus.Invalid;
                        objTemplate.Reason = Truncate(objException.Message, 1000);
                        objTemplate.LastError = "";
                        objTemplate.NextAttemptAt = null;
                        objTemplate.StatusChangedAt = dtNowUtc;
                    }
                    else
                    {
                        Fail(objTemplate, objException.Message, dtNowUtc);
                    }
                    AddEvent(objCompany, WhatsAppAccountEventKind.TemplateSubmitted,
                        $"Modelo {objTemplate.Name} (campanha {sCampaign}): falhou. {objException.Message}",
                        dtNowUtc, objException.Calls);
                    if (objException.MetaCode is 0 or 190 or 10 or 200)
                    {
                        await WhatsAppAlerts.EnqueueAsync(_objDbContext, objCompany.Id, "token",
                            $"WhatsApp — {objCompany.Name}: token sem acesso",
                            "O texto de uma campanha não foi para a análise da Meta: " + objException.Message,
                            dtNowUtc, objCancellationToken);
                    }
                    await _objDbContext.SaveChangesAsync(objCancellationToken);
                    _objLogger.LogWarning("WhatsApp: modelo {Nome} da empresa {Slug} não foi criado: {Motivo}",
                        objTemplate.Name, objCompany.Slug, objException.Message);

                    // Token, permissão ou Meta fora do ar: os próximos falhariam igual. Fica para a próxima volta.
                    if (!bTextProblem)
                    {
                        return;
                    }
                    continue;
                }

                await _objDbContext.SaveChangesAsync(objCancellationToken);
            }
        }

        /// <summary>Confere na Meta os modelos em análise (e, com menos frequência, os aprovados e pausados).</summary>
        private async Task RefreshStatusesAsync(
            Company objCompany, string sAccessToken, List<WhatsAppTemplate> objTemplates, DateTime dtNowUtc,
            CancellationToken objCancellationToken)
        {
            DateTime dtPendingLimit = dtNowUtc.AddMinutes(-Math.Max(1, _objOptions.PendingCheckMinutes));
            DateTime dtApprovedLimit = dtNowUtc.AddHours(-Math.Max(1, _objOptions.ApprovedCheckHours));
            List<WhatsAppTemplate> objDue = objTemplates
                .Where(template => template.MetaTemplateId.Length > 0)
                .Where(template =>
                    ((template.Status == WhatsAppTemplateStatus.Pending
                        // Recusado na criação: busca o motivo uma vez.
                        || (template.Status == WhatsAppTemplateStatus.Rejected && template.Reason.Length == 0))
                        && (template.CheckedAt is null || template.CheckedAt <= dtPendingLimit))
                    || ((template.Status == WhatsAppTemplateStatus.Approved || template.Status == WhatsAppTemplateStatus.Paused)
                        && (template.CheckedAt is null || template.CheckedAt <= dtApprovedLimit)))
                .OrderBy(template => template.CheckedAt ?? DateTime.MinValue)
                .Take(Math.Max(1, _objOptions.MaxChecksPerTick))
                .ToList();

            foreach (WhatsAppTemplate objTemplate in objDue)
            {
                string sBefore = WhatsAppTemplateLookup.StatusLabel(objTemplate.Status);
                objTemplate.CheckedAt = dtNowUtc;
                try
                {
                    (WhatsAppTemplateInfo objInfo, List<WhatsAppCall> objCalls) = await _objClient.GetTemplateAsync(
                        objTemplate.MetaTemplateId, sAccessToken, objCancellationToken);
                    objTemplate.LastError = "";
                    if (ApplyStatus(objTemplate, objInfo, dtNowUtc))
                    {
                        AddEvent(objCompany, WhatsAppAccountEventKind.TemplateStatusChanged,
                            $"Modelo {objTemplate.Name}: {sBefore} → {WhatsAppTemplateLookup.StatusLabel(objTemplate.Status)}."
                            + (objTemplate.Reason.Length > 0 ? " " + objTemplate.Reason : ""), dtNowUtc, objCalls);
                        if (WhatsAppTemplateLookup.NeedsAlert(objTemplate.Status))
                        {
                            await WhatsAppAlerts.EnqueueAsync(_objDbContext, objCompany.Id, "template:" + objTemplate.Name,
                                $"WhatsApp — {objCompany.Name}: texto {WhatsAppTemplateLookup.StatusLabel(objTemplate.Status)}",
                                $"O texto {objTemplate.Name} ficou {WhatsAppTemplateLookup.StatusLabel(objTemplate.Status)} na "
                                + "Meta. " + objTemplate.Reason + " Enquanto isso, as campanhas usam o último texto aprovado; "
                                + "sem nenhum, os clientes ficam só no PDF do gestor.", dtNowUtc, objCancellationToken);
                        }
                    }
                }
                catch (WhatsAppApiException objException) when (objException.MetaCode == 100)
                {
                    // O id não existe mais: alguém apagou o modelo no WhatsApp Manager.
                    objTemplate.Status = WhatsAppTemplateStatus.Disabled;
                    objTemplate.MetaStatus = "DELETED";
                    objTemplate.Reason = "O modelo não existe mais na Meta (foi apagado no WhatsApp Manager). "
                        + "O sistema cria um novo com o mesmo texto.";
                    objTemplate.StatusChangedAt = dtNowUtc;
                    AddEvent(objCompany, WhatsAppAccountEventKind.TemplateStatusChanged,
                        $"Modelo {objTemplate.Name}: {sBefore} → desativado. {objTemplate.Reason}", dtNowUtc, objException.Calls);
                }
                catch (WhatsAppApiException objException)
                {
                    // Token ou Meta fora do ar: guarda o motivo e para; a próxima volta tenta de novo.
                    objTemplate.LastError = Truncate(objException.Message, 1000);
                    await _objDbContext.SaveChangesAsync(objCancellationToken);
                    return;
                }
                await _objDbContext.SaveChangesAsync(objCancellationToken);
            }
        }

        /// <summary>
        /// De 6 em 6 horas, lê o número na Meta: nome, qualidade e limite (o limite do dia do envio vem daqui). Token
        /// recusado: a conexão deixa de estar aprovada (os envios param com o motivo) e o super admin é avisado.
        /// Devolve false quando a conexão caiu, para não seguir com os modelos.
        /// </summary>
        private async Task<bool> CheckPhoneAsync(
            Company objCompany, string sAccessToken, DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            CompanyWhatsApp objWhatsApp = objCompany.WhatsApp;
            if (objWhatsApp.CheckedAt is DateTime dtChecked
                && dtChecked > dtNowUtc.AddHours(-Math.Max(1, _objOptions.PhoneCheckHours)))
            {
                return true;
            }

            objWhatsApp.CheckedAt = dtNowUtc;
            try
            {
                (WhatsAppPhoneInfo objPhone, List<WhatsAppCall> objCalls) = await _objClient.CheckConnectionAsync(
                    objWhatsApp.PhoneNumberId, objWhatsApp.BusinessAccountId, sAccessToken, objCancellationToken);
                string sQualityBefore = objWhatsApp.QualityRating;
                string sLimitBefore = objWhatsApp.MessagingLimit;
                objWhatsApp.DisplayPhoneNumber = Truncate(objPhone.DisplayPhoneNumber, 40);
                objWhatsApp.DisplayName = Truncate(objPhone.VerifiedName, 120);
                objWhatsApp.QualityRating = Truncate(objPhone.QualityRating, 20);
                objWhatsApp.MessagingLimit = Truncate(objPhone.MessagingLimit, 30);
                objWhatsApp.LastError = "";
                if (objWhatsApp.QualityRating != sQualityBefore || objWhatsApp.MessagingLimit != sLimitBefore)
                {
                    AddEvent(objCompany, WhatsAppAccountEventKind.PhoneUpdated,
                        $"Conferência do número: qualidade {sQualityBefore} → {objWhatsApp.QualityRating}, limite "
                        + $"{sLimitBefore} → {objWhatsApp.MessagingLimit}.", dtNowUtc, objCalls);
                }
                if (objWhatsApp.QualityRating is "YELLOW" or "RED" && objWhatsApp.QualityRating != sQualityBefore)
                {
                    await WhatsAppAlerts.EnqueueAsync(_objDbContext, objCompany.Id, "quality:" + objWhatsApp.QualityRating,
                        $"WhatsApp — {objCompany.Name}: qualidade {(objWhatsApp.QualityRating == "RED" ? "baixa" : "média")}",
                        $"A Meta baixou a qualidade do número para {objWhatsApp.QualityRating}. Muitos bloqueios ou "
                        + "denúncias baixam o limite de envios e podem pausar os textos. Revise os textos e a frequência "
                        + "das campanhas.", dtNowUtc, objCancellationToken);
                }
                await _objDbContext.SaveChangesAsync(objCancellationToken);
                return true;
            }
            catch (WhatsAppApiException objException) when (objException.MetaCode is 0 or 190 or 10 or 200 or 100)
            {
                // Token vencido ou sem acesso: a conexão cai até alguém testar de novo com um token válido.
                objWhatsApp.CheckPassed = false;
                objWhatsApp.LastError = Truncate(objException.Message, 500);
                AddEvent(objCompany, WhatsAppAccountEventKind.ConnectionTested, "",
                    "Conferência automática falhou: " + objException.Message, dtNowUtc, objException.Calls);
                await WhatsAppAlerts.EnqueueAsync(_objDbContext, objCompany.Id, "token",
                    $"WhatsApp — {objCompany.Name}: conexão com a Meta caiu",
                    "A conferência automática do número falhou: " + objException.Message + " Enquanto isso, as campanhas "
                    + "vão só por e-mail para o gestor. Troque o token e teste a conexão de novo.", dtNowUtc,
                    objCancellationToken);
                await _objDbContext.SaveChangesAsync(objCancellationToken);
                _objLogger.LogWarning("WhatsApp: conexão da empresa {Slug} caiu: {Motivo}", objCompany.Slug, objException.Message);
                return false;
            }
            catch (WhatsAppApiException objException)
            {
                // Meta fora do ar: tenta na próxima conferência, sem derrubar a conexão.
                objWhatsApp.LastError = Truncate(objException.Message, 500);
                await _objDbContext.SaveChangesAsync(objCancellationToken);
                return true;
            }
        }

        /// <summary>A Meta aceitou o pedido (ou o modelo já existia): guarda o id e a situação.</summary>
        private static void Accept(WhatsAppTemplate objTemplate, WhatsAppTemplateInfo objInfo, DateTime dtNowUtc)
        {
            objTemplate.MetaTemplateId = Truncate(objInfo.Id, 30);
            objTemplate.SubmittedAt = dtNowUtc;
            objTemplate.CheckedAt = dtNowUtc;
            objTemplate.LastError = "";
            objTemplate.NextAttemptAt = null;
            // A criação devolve a situação (em geral PENDING); sem ela, fica em análise.
            ApplyStatus(objTemplate, objInfo with { Status = objInfo.Status.Length > 0 ? objInfo.Status : "PENDING" }, dtNowUtc);
            objTemplate.StatusChangedAt = dtNowUtc;
        }

        /// <summary>Aplica a situação da Meta. Devolve true se algo mudou.</summary>
        private static bool ApplyStatus(WhatsAppTemplate objTemplate, WhatsAppTemplateInfo objInfo, DateTime dtNowUtc) =>
            WhatsAppTemplateLookup.ApplyMetaStatus(
                objTemplate, objInfo.Status, objInfo.Category, objInfo.RejectedReason, dtNowUtc);

        /// <summary>Falha ao falar com a Meta: tenta de novo em 5 min, 15 min, 1 h e depois a cada 6 h.</summary>
        private static void Fail(WhatsAppTemplate objTemplate, string sError, DateTime dtNowUtc)
        {
            objTemplate.Status = WhatsAppTemplateStatus.Error;
            objTemplate.LastError = Truncate(sError, 1000);
            objTemplate.StatusChangedAt = dtNowUtc;
            objTemplate.NextAttemptAt = dtNowUtc + objTemplate.Attempts switch
            {
                <= 1 => TimeSpan.FromMinutes(5),
                2 => TimeSpan.FromMinutes(15),
                3 => TimeSpan.FromHours(1),
                _ => TimeSpan.FromHours(6),
            };
        }

        private static string UniqueName(string sName, IReadOnlyList<WhatsAppTemplate> objTemplates)
        {
            HashSet<string> objUsed = objTemplates.Select(template => template.Name).ToHashSet();
            if (!objUsed.Contains(sName))
            {
                return sName;
            }
            for (int iSuffix = 2; ; iSuffix++)
            {
                string sCandidate = $"{sName}_{iSuffix}";
                if (!objUsed.Contains(sCandidate))
                {
                    return sCandidate;
                }
            }
        }

        private void AddEvent(
            Company objCompany, string sKind, string sDetails, DateTime dtNowUtc, IReadOnlyList<WhatsAppCall> objCalls)
            => AddEvent(objCompany, sKind, "", sDetails, dtNowUtc, objCalls);

        private void AddEvent(
            Company objCompany, string sKind, string sUsername, string sDetails, DateTime dtNowUtc,
            IReadOnlyList<WhatsAppCall> objCalls)
        {
            _objDbContext.WhatsAppAccountEvents.Add(new WhatsAppAccountEvent
            {
                IDCompany = objCompany.Id,
                Kind = sKind,
                Username = sUsername,
                Details = Truncate(sDetails, 1000),
                RequestJson = objCalls.Count == 0 ? null : WhatsAppCallLog.RequestsJson(objCalls),
                ResponseJson = objCalls.Count == 0 ? null : WhatsAppCallLog.ResponsesJson(objCalls),
                CreatedAt = dtNowUtc,
            });
        }

        private static string Truncate(string sValue, int iMax) => sValue.Length <= iMax ? sValue : sValue[..iMax];
    }
}
