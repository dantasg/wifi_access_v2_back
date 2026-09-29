using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Models.Campaigns;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifiService.Campaigns
{
    public sealed class CampaignEngineOptions
    {
        public const string SectionName = "Campaigns";

        /// <summary>Ritmo de cada execução, em mensagens por minuto (o WhatsApp vai exigir um limite).</summary>
        public int MessagesPerMinute { get; set; } = 600;

        /// <summary>De quanto em quanto tempo o serviço confere agendas e processa um lote.</summary>
        public int TickSeconds { get; set; } = 5;

        /// <summary>Destinatários gravados por vez na seleção (o total aparece crescendo na tela).</summary>
        public int SelectionChunkSize { get; set; } = 1000;
    }

    /// <summary>
    /// O motor das campanhas, chamado a cada poucos segundos pelo <see cref="CampaignWorker"/>:
    /// cria as execuções que venceram, escolhe os destinatários e processa um lote de cada execução em
    /// andamento. Todo o estado mora no banco — cada destinatário tem o seu status —, então pausar,
    /// retomar e sobreviver a um reinício do serviço é continuar dos pendentes.
    /// </summary>
    public sealed class CampaignEngine
    {
        private static readonly string[] s_arrDelivered = [.. CampaignRecipientStatus.Delivered];

        private readonly AppDbContext _objDbContext;
        private readonly IMessageChannel _objChannel;
        private readonly CampaignEngineOptions _objOptions;
        private readonly ILogger<CampaignEngine> _objLogger;

        public CampaignEngine(
            AppDbContext objDbContext,
            IMessageChannel objChannel,
            IOptions<CampaignEngineOptions> objOptions,
            ILogger<CampaignEngine> objLogger)
        {
            _objDbContext = objDbContext;
            _objChannel = objChannel;
            _objOptions = objOptions.Value;
            _objLogger = objLogger;
        }

        public async Task TickAsync(DateTime dtNowUtc, CancellationToken objCancellationToken = default)
        {
            await ScheduleDueAsync(dtNowUtc, objCancellationToken);
            await SelectPendingRunsAsync(dtNowUtc, objCancellationToken);
            await FinalizeCancelledRunsAsync(dtNowUtc, objCancellationToken);

            int iPorVolta = Math.Max(1, _objOptions.MessagesPerMinute * _objOptions.TickSeconds / 60);
            List<Guid> objRunning = await _objDbContext.CampaignRuns.AsNoTracking()
                .Where(run => run.Status == CampaignRunStatus.Running)
                .OrderBy(run => run.CreatedAt)
                .Select(run => run.Id)
                .ToListAsync(objCancellationToken);
            foreach (Guid objRunId in objRunning)
            {
                await ProcessRunAsync(objRunId, iPorVolta, objCancellationToken);
            }
        }

        // ------------------------------------------------------------------ Agenda

        /// <summary>
        /// Cria as execuções das campanhas cujo horário chegou. Sempre no horário cadastrado (D10): se o
        /// serviço estava fora e o dia ainda é o mesmo, dispara agora; se o dia já virou, registra
        /// "perdida" e segue para o próximo horário da agenda.
        /// </summary>
        public async Task<int> ScheduleDueAsync(DateTime dtNowUtc, CancellationToken objCancellationToken = default)
        {
            List<Campaign> objDue = await _objDbContext.Campaigns
                .Where(campaign => campaign.Status == CampaignStatus.Active
                    && campaign.NextRunAt != null && campaign.NextRunAt <= dtNowUtc)
                .ToListAsync(objCancellationToken);
            if (objDue.Count == 0)
            {
                return 0;
            }

            List<Guid> objCompanyIds = objDue.Select(campaign => campaign.IDCompany).Distinct().ToList();
            Dictionary<Guid, string> objZones = await _objDbContext.Companies.AsNoTracking()
                .Where(company => objCompanyIds.Contains(company.Id))
                .ToDictionaryAsync(company => company.Id, company => company.TimeZone, objCancellationToken);
            HashSet<(Guid, string)> objEnabled = (await _objDbContext.CompanyCampaignKinds.AsNoTracking()
                    .Where(kind => objCompanyIds.Contains(kind.IDCompany))
                    .Select(kind => new { kind.IDCompany, kind.Kind })
                    .ToListAsync(objCancellationToken))
                .Select(kind => (kind.IDCompany, kind.Kind))
                .ToHashSet();

            int iCriadas = 0;
            // Na mesma volta, a de maior prioridade escolhe os destinatários primeiro (D11).
            foreach (Campaign objCampaign in objDue
                .OrderBy(campaign => CampaignKind.Priority(campaign.Kind))
                .ThenBy(campaign => campaign.NextRunAt))
            {
                if (!objEnabled.Contains((objCampaign.IDCompany, objCampaign.Kind)))
                {
                    // Tipo desligado para a empresa (D6): fica sem agenda até ser religado.
                    objCampaign.NextRunAt = null;
                    continue;
                }

                CampaignVersion? objVersion = await _objDbContext.CampaignVersions.AsNoTracking()
                    .FirstOrDefaultAsync(
                        version => version.IDCampaign == objCampaign.Id && version.Number == objCampaign.CurrentVersion,
                        objCancellationToken);
                if (objVersion is null)
                {
                    _objLogger.LogError("Campanha {Id} sem a versão {Versao}: disparo suspenso.",
                        objCampaign.Id, objCampaign.CurrentVersion);
                    objCampaign.NextRunAt = null;
                    continue;
                }

                TimeZoneInfo objZone = CompanyTimeZone.Resolve(objZones.GetValueOrDefault(objCampaign.IDCompany));
                CampaignConfig objConfig = CampaignConfig.FromJson(objCampaign.ConfigJson);
                DateOnly dtHoje = CompanyTimeZone.Today(objZone, dtNowUtc);

                // Cada volta anda um horário da agenda; o limite só protege contra laço infinito.
                for (int iGuard = 0; iGuard < 1000 && objCampaign.NextRunAt is DateTime dtOccurrence
                    && dtOccurrence <= dtNowUtc; iGuard++)
                {
                    DateOnly dtDia = CompanyTimeZone.Today(objZone, dtOccurrence);
                    bool bPerdida = dtDia < dtHoje;
                    // Uma execução por campanha por dia (D1): mudar o horário para mais tarde no mesmo
                    // dia, depois de já ter disparado, não dispara de novo.
                    bool bJaExiste = await _objDbContext.CampaignRuns.AnyAsync(
                        run => run.IDCampaign == objCampaign.Id && run.LocalDate == dtDia,
                        objCancellationToken);
                    if (!bJaExiste)
                    {
                        _objDbContext.CampaignRuns.Add(new CampaignRun
                        {
                            IDCampaign = objCampaign.Id,
                            IDCompany = objCampaign.IDCompany,
                            IDCampaignVersion = objVersion.Id,
                            VersionNumber = objVersion.Number,
                            ScheduledFor = dtOccurrence,
                            LocalDate = dtDia,
                            Status = bPerdida ? CampaignRunStatus.Missed : CampaignRunStatus.Selecting,
                            Simulation = true,
                            CreatedAt = dtNowUtc,
                            FinishedAt = bPerdida ? dtNowUtc : null,
                            Error = bPerdida ? "O serviço estava fora do ar no horário e o dia já virou." : null,
                        });
                        iCriadas++;
                    }
                    if (!bPerdida)
                    {
                        objCampaign.LastRunAt = dtOccurrence;
                    }

                    objCampaign.NextRunAt = CampaignCalendar.NextOccurrence(
                        objCampaign.Kind, objConfig, objZone, dtOccurrence);
                    if (objCampaign.NextRunAt is null)
                    {
                        objCampaign.Status = CampaignStatus.Finished;
                    }
                }
            }

            await _objDbContext.SaveChangesAsync(objCancellationToken);
            return iCriadas;
        }

        // ---------------------------------------------------------------- Seleção

        private async Task SelectPendingRunsAsync(DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            var objSelecting = await _objDbContext.CampaignRuns.AsNoTracking()
                .Where(run => run.Status == CampaignRunStatus.Selecting)
                .Join(_objDbContext.Campaigns, run => run.IDCampaign, campaign => campaign.Id,
                    (run, campaign) => new { run.Id, campaign.Kind, run.ScheduledFor })
                .ToListAsync(objCancellationToken);

            foreach (var objRun in objSelecting
                .OrderBy(run => CampaignKind.Priority(run.Kind))
                .ThenBy(run => run.ScheduledFor))
            {
                await SelectRecipientsAsync(objRun.Id, dtNowUtc, objCancellationToken);
            }
        }

        private sealed record AudienceMember(
            Guid Id, string Phone, string Name, DateOnly? BirthDate, DateOnly FirstVisitDate,
            int VisitCount, Guid? IDLastUnit);

        /// <summary>
        /// Escolhe os destinatários da execução e monta a mensagem de cada um. Grava em blocos: se o
        /// serviço cair no meio, continua de onde parou sem repetir ninguém (um cliente por execução, D1).
        /// </summary>
        public async Task SelectRecipientsAsync(Guid objRunId, DateTime dtNowUtc, CancellationToken objCancellationToken = default)
        {
            _objDbContext.ChangeTracker.Clear();
            CampaignRun? objRun = await _objDbContext.CampaignRuns
                .FirstOrDefaultAsync(run => run.Id == objRunId, objCancellationToken);
            if (objRun is null || objRun.Status != CampaignRunStatus.Selecting)
            {
                return;
            }

            Campaign objCampaign = await _objDbContext.Campaigns.AsNoTracking()
                .FirstAsync(campaign => campaign.Id == objRun.IDCampaign, objCancellationToken);
            CampaignVersion objVersion = await _objDbContext.CampaignVersions.AsNoTracking()
                .FirstAsync(version => version.Id == objRun.IDCampaignVersion, objCancellationToken);
            Company objCompany = await _objDbContext.Companies.AsNoTracking()
                .FirstAsync(company => company.Id == objRun.IDCompany, objCancellationToken);
            // D13: a execução roda com a versão com que foi criada, mesmo que a campanha mude depois.
            CampaignConfig objConfig = CampaignConfig.FromJson(objVersion.ConfigJson);

            objRun.StartedAt ??= dtNowUtc;
            await _objDbContext.SaveChangesAsync(objCancellationToken);

            Dictionary<Guid, string> objUnitNames = await _objDbContext.Units.AsNoTracking()
                .Where(unit => unit.IDCompany == objRun.IDCompany)
                .ToDictionaryAsync(unit => unit.Id, unit => unit.Name, objCancellationToken);

            List<AudienceMember> objAudience = await CampaignAudience
                .Query(_objDbContext, objRun.IDCompany, objCampaign.Id, objCampaign.Kind, objConfig,
                    objRun.LocalDate, objRun.ScheduledFor)
                .AsNoTracking()
                .Select(customer => new AudienceMember(
                    customer.Id, customer.Phone, customer.Name, customer.BirthDate, customer.FirstVisitDate,
                    customer.VisitCount, customer.IDLastUnit))
                .ToListAsync(objCancellationToken);

            // Retomada: quem já está nesta execução não entra de novo.
            HashSet<Guid> objJaNaExecucao = (await _objDbContext.CampaignRecipients.AsNoTracking()
                    .Where(recipient => recipient.IDRun == objRunId)
                    .Select(recipient => recipient.IDCustomer)
                    .ToListAsync(objCancellationToken))
                .ToHashSet();

            // D11: no máximo uma mensagem por cliente por dia, somando as campanhas da empresa.
            var objHojeOutras = await (
                from recipient in _objDbContext.CampaignRecipients.AsNoTracking()
                join run in _objDbContext.CampaignRuns.AsNoTracking() on recipient.IDRun equals run.Id
                join campaign in _objDbContext.Campaigns.AsNoTracking() on run.IDCampaign equals campaign.Id
                where run.IDCompany == objRun.IDCompany && run.LocalDate == objRun.LocalDate
                    && run.Id != objRunId && s_arrDelivered.Contains(recipient.Status)
                select new { recipient.Id, recipient.IDCustomer, recipient.Status, RunId = run.Id, campaign.Kind, campaign.Name })
                .ToListAsync(objCancellationToken);
            var objOutraPorCliente = objHojeOutras
                .GroupBy(item => item.IDCustomer)
                .ToDictionary(group => group.Key, group => group.First());

            int iPrioridade = CampaignKind.Priority(objCampaign.Kind);
            int? iMarco = objCampaign.Kind == CampaignKind.FrequentCustomer ? objConfig.VisitMilestone ?? 5 : null;
            int iBloco = Math.Max(1, _objOptions.SelectionChunkSize);

            List<CampaignRecipient> objBloco = new(iBloco);
            int iIgnoradosNoBloco = 0;
            foreach (AudienceMember objMember in objAudience)
            {
                if (objJaNaExecucao.Contains(objMember.Id))
                {
                    continue;
                }

                string sMensagem = CampaignMessage.Render(objConfig.Message, new CampaignMessageData(
                    objMember.Name,
                    objCompany.Name,
                    objMember.IDLastUnit is Guid objUnitId ? objUnitNames.GetValueOrDefault(objUnitId, "") : "",
                    CampaignMessage.AgeOn(objMember.BirthDate, objRun.LocalDate),
                    CampaignMessage.FullYearsBetween(objMember.FirstVisitDate, objRun.LocalDate)));

                CampaignRecipient objRecipient = new CampaignRecipient
                {
                    IDRun = objRunId,
                    IDCustomer = objMember.Id,
                    Phone = objMember.Phone,
                    Name = objMember.Name,
                    Message = sMensagem.Length <= 4000 ? sMensagem : sMensagem[..4000],
                    Status = CampaignRecipientStatus.Pending,
                    Milestone = iMarco is int iStep ? objMember.VisitCount / iStep * iStep : null,
                    CreatedAt = dtNowUtc,
                };

                if (objOutraPorCliente.TryGetValue(objMember.Id, out var objOutra))
                {
                    bool bOutraMenosImportante = CampaignKind.Priority(objOutra.Kind) > iPrioridade;
                    if (objOutra.Status == CampaignRecipientStatus.Pending && bOutraMenosImportante)
                    {
                        // A outra ainda não saiu e é menos importante: esta ocupa o lugar dela.
                        await SubstituirAsync(objOutra.Id, objOutra.RunId, objCampaign.Name, dtNowUtc, objCancellationToken);
                    }
                    else
                    {
                        objRecipient.Status = CampaignRecipientStatus.Ignored;
                        objRecipient.Reason = $"Limite do dia: já recebe \"{objOutra.Name}\" hoje.";
                        objRecipient.ProcessedAt = dtNowUtc;
                        iIgnoradosNoBloco++;
                    }
                }

                objBloco.Add(objRecipient);
                if (objBloco.Count >= iBloco)
                {
                    if (!await GravarBlocoAsync(objRunId, objBloco, iIgnoradosNoBloco, objCancellationToken))
                    {
                        return; // cancelada no meio da seleção
                    }
                    iIgnoradosNoBloco = 0;
                }
            }

            if (!await GravarBlocoAsync(objRunId, objBloco, iIgnoradosNoBloco, objCancellationToken))
            {
                return;
            }

            objRun = await _objDbContext.CampaignRuns.FirstAsync(run => run.Id == objRunId, objCancellationToken);
            if (objRun.Status == CampaignRunStatus.Selecting)
            {
                bool bHaPendentes = objRun.TotalCount > objRun.IgnoredCount;
                objRun.Status = bHaPendentes ? CampaignRunStatus.Running : CampaignRunStatus.Completed;
                objRun.FinishedAt = bHaPendentes ? null : dtNowUtc;
                await _objDbContext.SaveChangesAsync(objCancellationToken);
            }
            _objLogger.LogInformation(
                "Campanha {Campanha}: execução {Execucao} com {Total} destinatário(s).",
                objCampaign.Name, objRunId, objRun.TotalCount);
        }

        /// <summary>Grava um bloco da seleção e soma no total. Devolve false se a execução foi cancelada.</summary>
        private async Task<bool> GravarBlocoAsync(
            Guid objRunId, List<CampaignRecipient> objBloco, int iIgnorados, CancellationToken objCancellationToken)
        {
            CampaignRun objRun = await _objDbContext.CampaignRuns.FirstAsync(run => run.Id == objRunId, objCancellationToken);
            if (objRun.Status != CampaignRunStatus.Selecting)
            {
                return false;
            }

            _objDbContext.CampaignRecipients.AddRange(objBloco);
            objRun.TotalCount += objBloco.Count;
            objRun.IgnoredCount += iIgnorados;
            // Salva mesmo com o bloco vazio: leva junto as substituições feitas em outras execuções.
            await _objDbContext.SaveChangesAsync(objCancellationToken);

            objBloco.Clear();
            // Sem isso o rastreamento cresce a cada bloco e uma seleção grande fica cada vez mais lenta.
            _objDbContext.ChangeTracker.Clear();
            return true;
        }

        private async Task SubstituirAsync(
            long lRecipientId, Guid objOutraRunId, string sCampanha, DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            CampaignRecipient? objOutro = await _objDbContext.CampaignRecipients
                .FirstOrDefaultAsync(recipient => recipient.Id == lRecipientId, objCancellationToken);
            if (objOutro is null || objOutro.Status != CampaignRecipientStatus.Pending)
            {
                return;
            }
            objOutro.Status = CampaignRecipientStatus.Ignored;
            objOutro.Reason = $"Substituída por \"{sCampanha}\" (limite de 1 mensagem por dia).";
            objOutro.ProcessedAt = dtNowUtc;

            CampaignRun objOutraRun = await _objDbContext.CampaignRuns
                .FirstAsync(run => run.Id == objOutraRunId, objCancellationToken);
            objOutraRun.IgnoredCount++;
            // Grava junto com o próximo bloco desta seleção.
        }

        // -------------------------------------------------------------- Processamento

        /// <summary>
        /// Processa até <paramref name="iMax"/> pendentes da execução, em ordem. Pausada ou cancelada
        /// entre um lote e outro: não faz nada — a retomada continua dos pendentes.
        /// </summary>
        public async Task<int> ProcessRunAsync(Guid objRunId, int iMax, CancellationToken objCancellationToken = default)
        {
            _objDbContext.ChangeTracker.Clear();
            CampaignRun? objRun = await _objDbContext.CampaignRuns
                .FirstOrDefaultAsync(run => run.Id == objRunId, objCancellationToken);
            if (objRun is null || objRun.Status != CampaignRunStatus.Running)
            {
                return 0;
            }

            string sCanal = CampaignConfig.FromJson(await _objDbContext.CampaignVersions.AsNoTracking()
                .Where(version => version.Id == objRun.IDCampaignVersion)
                .Select(version => version.ConfigJson)
                .FirstAsync(objCancellationToken)).Channel;

            List<CampaignRecipient> objLote = await _objDbContext.CampaignRecipients
                .Where(recipient => recipient.IDRun == objRunId && recipient.Status == CampaignRecipientStatus.Pending)
                .OrderBy(recipient => recipient.Id)
                .Take(iMax)
                .ToListAsync(objCancellationToken);

            foreach (CampaignRecipient objRecipient in objLote)
            {
                MessageSendResult objResult;
                try
                {
                    objResult = await _objChannel.SendAsync(objRecipient, sCanal, objCancellationToken);
                }
                catch (Exception objException) when (objException is not OperationCanceledException)
                {
                    // Um envio que quebra não derruba o lote: fica registrado como falha.
                    objResult = new MessageSendResult(CampaignRecipientStatus.Failed, objException.Message);
                }

                objRecipient.Status = objResult.Status;
                objRecipient.Reason = objResult.Reason is { Length: > 300 } sLongo ? sLongo[..300] : objResult.Reason;
                objRecipient.ProcessedAt = DateTime.UtcNow;
                switch (objResult.Status)
                {
                    case CampaignRecipientStatus.Sent: objRun.SentCount++; break;
                    case CampaignRecipientStatus.Simulated: objRun.SimulatedCount++; break;
                    case CampaignRecipientStatus.Failed: objRun.FailedCount++; break;
                    default: objRun.IgnoredCount++; break;
                }
            }

            // Veio menos que o máximo: não sobrou pendente, a execução terminou.
            if (objLote.Count < iMax)
            {
                objRun.Status = CampaignRunStatus.Completed;
                objRun.FinishedAt = DateTime.UtcNow;
            }

            await _objDbContext.SaveChangesAsync(objCancellationToken);
            return objLote.Count;
        }

        /// <summary>Execuções canceladas: os pendentes viram "cancelado" (em blocos).</summary>
        private async Task FinalizeCancelledRunsAsync(DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            List<Guid> objCancelled = await _objDbContext.CampaignRuns.AsNoTracking()
                .Where(run => run.Status == CampaignRunStatus.Cancelled
                    && _objDbContext.CampaignRecipients.Any(recipient =>
                        recipient.IDRun == run.Id && recipient.Status == CampaignRecipientStatus.Pending))
                .Select(run => run.Id)
                .ToListAsync(objCancellationToken);

            foreach (Guid objRunId in objCancelled)
            {
                while (true)
                {
                    _objDbContext.ChangeTracker.Clear();
                    List<CampaignRecipient> objBloco = await _objDbContext.CampaignRecipients
                        .Where(recipient => recipient.IDRun == objRunId && recipient.Status == CampaignRecipientStatus.Pending)
                        .OrderBy(recipient => recipient.Id)
                        .Take(1000)
                        .ToListAsync(objCancellationToken);
                    if (objBloco.Count == 0)
                    {
                        break;
                    }

                    foreach (CampaignRecipient objRecipient in objBloco)
                    {
                        objRecipient.Status = CampaignRecipientStatus.Cancelled;
                        objRecipient.Reason = "Execução cancelada.";
                        objRecipient.ProcessedAt = dtNowUtc;
                    }
                    CampaignRun objRun = await _objDbContext.CampaignRuns.FirstAsync(run => run.Id == objRunId, objCancellationToken);
                    objRun.CancelledCount += objBloco.Count;
                    objRun.FinishedAt ??= dtNowUtc;
                    await _objDbContext.SaveChangesAsync(objCancellationToken);
                }
            }
        }
    }
}
