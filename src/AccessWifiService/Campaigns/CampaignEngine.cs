using System.Globalization;
using System.Text;
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

        /// <summary>De quanto em quanto tempo o serviço confere agendas e manda os e-mails.</summary>
        public int TickSeconds { get; set; } = 5;

        /// <summary>Destinatários gravados por vez na seleção (o total aparece crescendo na tela).</summary>
        public int SelectionChunkSize { get; set; } = 1000;

        /// <summary>Tentativas de mandar o e-mail de uma unidade antes de desistir (D26).</summary>
        public int EmailMaxAttempts { get; set; } = 3;

        /// <summary>Espera entre uma tentativa e outra, em minutos.</summary>
        public int EmailRetryMinutes { get; set; } = 5;
    }

    /// <summary>
    /// O motor das campanhas, chamado a cada poucos segundos pelo <see cref="CampaignWorker"/>: cria as
    /// execuções que venceram, escolhe os clientes e manda, para cada unidade, um e-mail com o PDF dos
    /// clientes dela (D17) — o gerente faz o contato. Todo o estado mora no banco (cada cliente e cada
    /// e-mail têm o seu status), então pausar, retomar e sobreviver a um reinício é continuar dos pendentes.
    /// </summary>
    public sealed class CampaignEngine
    {
        private static readonly string[] s_arrDelivered = [.. CampaignRecipientStatus.Delivered];
        private static readonly string[] s_arrPending = [CampaignRecipientStatus.Pending];

        private readonly AppDbContext _objDbContext;
        private readonly IEmailSender _objEmailSender;
        private readonly CampaignEngineOptions _objOptions;
        private readonly ILogger<CampaignEngine> _objLogger;

        public CampaignEngine(
            AppDbContext objDbContext,
            IEmailSender objEmailSender,
            IOptions<CampaignEngineOptions> objOptions,
            ILogger<CampaignEngine> objLogger)
        {
            _objDbContext = objDbContext;
            _objEmailSender = objEmailSender;
            _objOptions = objOptions.Value;
            _objLogger = objLogger;
        }

        public async Task TickAsync(DateTime dtNowUtc, CancellationToken objCancellationToken = default)
        {
            await ScheduleDueAsync(dtNowUtc, objCancellationToken);
            await SelectPendingRunsAsync(dtNowUtc, objCancellationToken);
            await FinalizeCancelledRunsAsync(dtNowUtc, objCancellationToken);

            List<Guid> objRunning = await _objDbContext.CampaignRuns.AsNoTracking()
                .Where(run => run.Status == CampaignRunStatus.Running)
                .OrderBy(run => run.CreatedAt)
                .Select(run => run.Id)
                .ToListAsync(objCancellationToken);
            foreach (Guid objRunId in objRunning)
            {
                await ProcessRunAsync(objRunId, dtNowUtc, objCancellationToken);
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
                            Simulation = false,
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
            Guid Id, string Phone, string Name, string Instagram, DateOnly? BirthDate, DateOnly FirstVisitDate,
            DateOnly LastVisitDate, int VisitCount, Guid? IDLastUnit);

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
                    customer.Id, customer.Phone, customer.Name, customer.Instagram, customer.BirthDate,
                    customer.FirstVisitDate, customer.LastVisitDate, customer.VisitCount, customer.IDLastUnit))
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

                int? iMarcoDoCliente = iMarco is int iStep ? objMember.VisitCount / iStep * iStep : null;
                (string sInfo, DateOnly? dtEvento, int? iIdade) = Detalhar(
                    objCampaign.Kind, objMember, objRun.LocalDate, iMarcoDoCliente);
                string sMensagem = CampaignMessage.Render(objConfig.Message, new CampaignMessageData(
                    objMember.Name,
                    objCompany.Name,
                    objMember.IDLastUnit is Guid objUnitId ? objUnitNames.GetValueOrDefault(objUnitId, "") : "",
                    iIdade,
                    CampaignMessage.FullYearsBetween(objMember.FirstVisitDate, objRun.LocalDate)));

                CampaignRecipient objRecipient = new CampaignRecipient
                {
                    IDRun = objRunId,
                    IDCustomer = objMember.Id,
                    // D17: vai para o gerente da unidade da última visita.
                    IDUnit = objMember.IDLastUnit,
                    Phone = objMember.Phone,
                    Name = objMember.Name,
                    Instagram = InstagramHandle.Normalize(objMember.Instagram),
                    Message = sMensagem.Length <= 4000 ? sMensagem : sMensagem[..4000],
                    Info = sInfo,
                    EventDate = dtEvento,
                    Status = CampaignRecipientStatus.Pending,
                    Milestone = iMarcoDoCliente,
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

        /// <summary>
        /// A coluna de informação do PDF e, no aniversário, o dia da semana em que o cliente faz anos e a
        /// idade que completa nele (a lista de segunda já traz quem faz anos na sexta).
        /// </summary>
        private static (string Info, DateOnly? EventDate, int? Age) Detalhar(
            string sKind, AudienceMember objMember, DateOnly dtDia, int? iMarco)
        {
            int? iIdade = CampaignMessage.AgeOn(objMember.BirthDate, dtDia);
            switch (sKind)
            {
                case CampaignKind.Birthday when objMember.BirthDate is DateOnly dtNascimento:
                    DateOnly dtAniversario = CampaignCalendar.BirthdayInRange(dtNascimento, dtDia) ?? dtDia;
                    int iAnos = CampaignMessage.FullYearsBetween(dtNascimento, dtAniversario);
                    return ($"{CampaignPdf.ShortDate(dtAniversario)} · {iAnos} anos", dtAniversario, iAnos);
                case CampaignKind.SignupAnniversary:
                    int iCadastro = CampaignMessage.FullYearsBetween(objMember.FirstVisitDate, dtDia);
                    return (iCadastro == 1 ? "1 ano de cadastro" : $"{iCadastro} anos de cadastro", null, iIdade);
                case CampaignKind.FrequentCustomer when iMarco is int iVisitas:
                    return ($"{iVisitas}ª visita", null, iIdade);
                case CampaignKind.WeMissYou:
                    return ($"Última visita em {objMember.LastVisitDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}",
                        null, iIdade);
                default:
                    return ("", null, iIdade);
            }
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

        // -------------------------------------------------------------- Envio

        /// <summary>
        /// Manda os e-mails da execução: um por unidade, com o PDF dos clientes dela (D17). O que falha
        /// tenta de novo depois de alguns minutos, até o limite (D26). Pausada ou cancelada: não faz nada —
        /// a retomada continua dos pendentes. Devolve quantos clientes foram nos e-mails desta volta.
        /// </summary>
        public async Task<int> ProcessRunAsync(Guid objRunId, DateTime dtNowUtc, CancellationToken objCancellationToken = default)
        {
            _objDbContext.ChangeTracker.Clear();
            CampaignRun? objRun = await _objDbContext.CampaignRuns
                .FirstOrDefaultAsync(run => run.Id == objRunId, objCancellationToken);
            if (objRun is null || objRun.Status != CampaignRunStatus.Running)
            {
                return 0;
            }

            Campaign objCampaign = await _objDbContext.Campaigns.AsNoTracking()
                .FirstAsync(campaign => campaign.Id == objRun.IDCampaign, objCancellationToken);
            await CriarEntregasAsync(objRun, objCampaign, dtNowUtc, objCancellationToken);

            List<CampaignDelivery> objVencidas = await _objDbContext.CampaignDeliveries
                .Where(delivery => delivery.IDRun == objRunId && delivery.Status == CampaignDeliveryStatus.Pending
                    && (delivery.NextAttemptAt == null || delivery.NextAttemptAt <= dtNowUtc))
                .OrderBy(delivery => delivery.UnitName)
                .ToListAsync(objCancellationToken);

            int iClientes = 0;
            foreach (CampaignDelivery objDelivery in objVencidas)
            {
                // Pausada ou cancelada pela tela no meio dos envios: para aqui.
                string sStatus = await _objDbContext.CampaignRuns.AsNoTracking()
                    .Where(run => run.Id == objRunId).Select(run => run.Status).FirstAsync(objCancellationToken);
                if (sStatus != CampaignRunStatus.Running)
                {
                    return iClientes;
                }
                iClientes += await EnviarAsync(objRun, objCampaign, objDelivery, dtNowUtc, objCancellationToken);
            }

            bool bHaPendentes = await _objDbContext.CampaignRecipients.AnyAsync(
                recipient => recipient.IDRun == objRunId && recipient.Status == CampaignRecipientStatus.Pending,
                objCancellationToken);
            string sAgora = await _objDbContext.CampaignRuns.AsNoTracking()
                .Where(run => run.Id == objRunId).Select(run => run.Status).FirstAsync(objCancellationToken);
            if (!bHaPendentes && sAgora == CampaignRunStatus.Running)
            {
                objRun.Status = CampaignRunStatus.Completed;
                objRun.FinishedAt = dtNowUtc;
                await _objDbContext.SaveChangesAsync(objCancellationToken);
            }
            return iClientes;
        }

        /// <summary>
        /// Uma entrega por unidade que tem cliente pendente. Unidade sem e-mail (ou cliente sem unidade):
        /// a entrega já nasce como falha, com o motivo, e os clientes dela também.
        /// </summary>
        private async Task CriarEntregasAsync(
            CampaignRun objRun, Campaign objCampaign, DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            var objGrupos = await _objDbContext.CampaignRecipients.AsNoTracking()
                .Where(recipient => recipient.IDRun == objRun.Id && recipient.Status == CampaignRecipientStatus.Pending)
                .GroupBy(recipient => recipient.IDUnit)
                .Select(group => new { IDUnit = group.Key, Total = group.Count() })
                .ToListAsync(objCancellationToken);
            if (objGrupos.Count == 0)
            {
                return;
            }

            List<Guid?> objJaTem = await _objDbContext.CampaignDeliveries.AsNoTracking()
                .Where(delivery => delivery.IDRun == objRun.Id)
                .Select(delivery => delivery.IDUnit)
                .ToListAsync(objCancellationToken);
            Dictionary<Guid, Unit> objUnits = await _objDbContext.Units.AsNoTracking()
                .Where(unit => unit.IDCompany == objRun.IDCompany)
                .ToDictionaryAsync(unit => unit.Id, objCancellationToken);

            foreach (var objGrupo in objGrupos.Where(group => !objJaTem.Contains(group.IDUnit)))
            {
                Unit? objUnit = objGrupo.IDUnit is Guid objUnitId ? objUnits.GetValueOrDefault(objUnitId) : null;
                string sEmail = objUnit?.Email.Trim() ?? "";
                CampaignDelivery objDelivery = new CampaignDelivery
                {
                    IDRun = objRun.Id,
                    IDUnit = objGrupo.IDUnit,
                    UnitName = objUnit?.Name ?? "Sem unidade",
                    Email = sEmail,
                    RecipientCount = objGrupo.Total,
                    FileName = CampaignDeliveryDocument.FileName(objCampaign.Kind, objUnit?.Slug ?? "", objRun.LocalDate),
                    CreatedAt = dtNowUtc,
                    NextAttemptAt = dtNowUtc,
                };

                string? sSemDestino = objUnit is null
                    ? "Clientes sem unidade: não há para quem mandar."
                    : sEmail.Length == 0
                        ? $"A unidade {objUnit.Name} não tem e-mail cadastrado (Unidades → editar → e-mail)."
                        : null;
                if (sSemDestino is not null)
                {
                    objDelivery.Status = CampaignDeliveryStatus.Failed;
                    objDelivery.Error = sSemDestino;
                    objDelivery.NextAttemptAt = null;
                    objRun.FailedCount += await MarcarClientesAsync(
                        objRun.Id, objGrupo.IDUnit, CampaignRecipientStatus.Failed, sSemDestino, dtNowUtc, objCancellationToken);
                    _objLogger.LogWarning("Campanha {Campanha}: {Motivo}", objCampaign.Name, sSemDestino);
                }
                _objDbContext.CampaignDeliveries.Add(objDelivery);
            }

            await _objDbContext.SaveChangesAsync(objCancellationToken);
        }

        /// <summary>Monta o PDF da unidade e manda o e-mail. Devolve quantos clientes foram (0 se falhou).</summary>
        private async Task<int> EnviarAsync(
            CampaignRun objRun, Campaign objCampaign, CampaignDelivery objDelivery, DateTime dtNowUtc,
            CancellationToken objCancellationToken)
        {
            CampaignPdfData objDados = await CampaignDeliveryDocument.LoadAsync(
                _objDbContext, objRun, objDelivery.IDUnit, s_arrPending, objCancellationToken);
            if (objDados.Rows.Count == 0)
            {
                // Os clientes saíram desta execução antes do envio (ex.: outra campanha ocupou o dia).
                objDelivery.Status = CampaignDeliveryStatus.Cancelled;
                objDelivery.RecipientCount = 0;
                objDelivery.NextAttemptAt = null;
                await _objDbContext.SaveChangesAsync(objCancellationToken);
                return 0;
            }

            string? sErro = null;
            try
            {
                byte[] arrPdf = CampaignPdf.Build(objDados);
                await _objEmailSender.SendAsync(
                    objDelivery.Email, Assunto(objDados), Corpo(objDados), arrPdf, objDelivery.FileName,
                    objCancellationToken);
            }
            catch (Exception objException) when (objException is not OperationCanceledException)
            {
                sErro = objException.Message;
            }

            objDelivery.Attempts++;
            objDelivery.RecipientCount = objDados.Rows.Count;
            if (sErro is null)
            {
                objDelivery.Status = CampaignDeliveryStatus.Sent;
                objDelivery.SentAt = dtNowUtc;
                objDelivery.Error = null;
                objDelivery.NextAttemptAt = null;
                objRun.SentCount += await MarcarClientesAsync(
                    objRun.Id, objDelivery.IDUnit, CampaignRecipientStatus.Sent, null, dtNowUtc, objCancellationToken);
                await _objDbContext.SaveChangesAsync(objCancellationToken);
                _objLogger.LogInformation(
                    "Campanha {Campanha}: PDF com {Total} cliente(s) enviado para {Email} (unidade {Unidade}).",
                    objCampaign.Name, objDados.Rows.Count, objDelivery.Email, objDelivery.UnitName);
                return objDados.Rows.Count;
            }

            objDelivery.Error = sErro.Length <= 500 ? sErro : sErro[..500];
            int iMaximo = Math.Max(1, _objOptions.EmailMaxAttempts);
            if (objDelivery.Attempts >= iMaximo)
            {
                objDelivery.Status = CampaignDeliveryStatus.Failed;
                objDelivery.NextAttemptAt = null;
                string sMotivo = $"O e-mail para {objDelivery.Email} não saiu ({objDelivery.Attempts} tentativas): {sErro}";
                objRun.FailedCount += await MarcarClientesAsync(
                    objRun.Id, objDelivery.IDUnit, CampaignRecipientStatus.Failed, sMotivo, dtNowUtc, objCancellationToken);
                _objLogger.LogError(
                    "Campanha {Campanha}: e-mail da unidade {Unidade} para {Email} desistido após {Tentativas} tentativas: {Erro}",
                    objCampaign.Name, objDelivery.UnitName, objDelivery.Email, objDelivery.Attempts, sErro);
            }
            else
            {
                objDelivery.NextAttemptAt = dtNowUtc.AddMinutes(Math.Max(1, _objOptions.EmailRetryMinutes));
                _objLogger.LogWarning(
                    "Campanha {Campanha}: e-mail da unidade {Unidade} para {Email} falhou (tentativa {Tentativa} de {Maximo}), tenta de novo às {Proxima:HH:mm} UTC: {Erro}",
                    objCampaign.Name, objDelivery.UnitName, objDelivery.Email, objDelivery.Attempts, iMaximo,
                    objDelivery.NextAttemptAt, sErro);
            }
            await _objDbContext.SaveChangesAsync(objCancellationToken);
            return 0;
        }

        /// <summary>Muda o status dos clientes pendentes de uma unidade na execução. Devolve quantos.</summary>
        private async Task<int> MarcarClientesAsync(
            Guid objRunId, Guid? objUnitId, string sStatus, string? sMotivo, DateTime dtNowUtc,
            CancellationToken objCancellationToken)
        {
            List<CampaignRecipient> objClientes = await _objDbContext.CampaignRecipients
                .Where(recipient => recipient.IDRun == objRunId && recipient.IDUnit == objUnitId
                    && recipient.Status == CampaignRecipientStatus.Pending)
                .ToListAsync(objCancellationToken);
            string? sCurto = sMotivo is { Length: > 300 } ? sMotivo[..300] : sMotivo;
            foreach (CampaignRecipient objCliente in objClientes)
            {
                objCliente.Status = sStatus;
                objCliente.Reason = sCurto;
                objCliente.ProcessedAt = dtNowUtc;
            }
            return objClientes.Count;
        }

        private static string Assunto(CampaignPdfData objDados) =>
            $"Campanha {objDados.CampaignName} — {objDados.UnitName} — " +
            $"{objDados.LocalDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} ({ClientesTexto(objDados.Rows.Count)})";

        private static string Corpo(CampaignPdfData objDados)
        {
            StringBuilder objCorpo = new StringBuilder();
            objCorpo.Append($"Olá, equipe da unidade {objDados.UnitName}!\r\n\r\n");
            objCorpo.Append($"Segue em anexo o PDF da campanha \"{objDados.CampaignName}\" com {ClientesTexto(objDados.Rows.Count)} ");
            objCorpo.Append($"para vocês entrarem em contato hoje ({CampaignPdf.LongDate(objDados.LocalDate)}).\r\n");
            if (objDados.Kind == CampaignKind.Birthday)
            {
                (DateOnly dtInicio, DateOnly dtFim) = CampaignCalendar.BirthdayRange(objDados.LocalDate);
                objCorpo.Append($"São os aniversariantes de {CampaignPdf.ShortDate(dtInicio)} a {CampaignPdf.ShortDate(dtFim)}.\r\n");
            }
            objCorpo.Append("\r\nMensagem para enviar:\r\n");
            objCorpo.Append(objDados.MessageTemplate.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            objCorpo.Append("\r\n\r\nNo PDF, clique no WhatsApp de cada cliente: a conversa abre com a mensagem pronta, ");
            objCorpo.Append("já com o nome dele. É só conferir e enviar.\r\n\r\n");
            objCorpo.Append("Mensagem automática do AccessWifi.");
            return objCorpo.ToString();
        }

        private static string ClientesTexto(int iTotal) => iTotal == 1 ? "1 cliente" : $"{iTotal} clientes";

        /// <summary>Execuções canceladas: os pendentes viram "cancelado" (em blocos), e os e-mails que não saíram também.</summary>
        private async Task FinalizeCancelledRunsAsync(DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            List<Guid> objCancelled = await _objDbContext.CampaignRuns.AsNoTracking()
                .Where(run => run.Status == CampaignRunStatus.Cancelled
                    && (_objDbContext.CampaignRecipients.Any(recipient =>
                            recipient.IDRun == run.Id && recipient.Status == CampaignRecipientStatus.Pending)
                        || _objDbContext.CampaignDeliveries.Any(delivery =>
                            delivery.IDRun == run.Id && delivery.Status == CampaignDeliveryStatus.Pending)))
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

                List<CampaignDelivery> objNaoSairam = await _objDbContext.CampaignDeliveries
                    .Where(delivery => delivery.IDRun == objRunId && delivery.Status == CampaignDeliveryStatus.Pending)
                    .ToListAsync(objCancellationToken);
                foreach (CampaignDelivery objDelivery in objNaoSairam)
                {
                    objDelivery.Status = CampaignDeliveryStatus.Cancelled;
                    objDelivery.NextAttemptAt = null;
                }
                await _objDbContext.SaveChangesAsync(objCancellationToken);
            }
        }
    }
}
