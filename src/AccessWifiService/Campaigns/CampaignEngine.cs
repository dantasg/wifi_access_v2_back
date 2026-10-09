using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Models.Campaigns;
using Models.DataBase;
using Models.Email;
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

            int iCreated = 0;
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
                DateOnly dtToday = CompanyTimeZone.Today(objZone, dtNowUtc);

                // Cada volta anda um horário da agenda; o limite só protege contra laço infinito.
                for (int iGuard = 0; iGuard < 1000 && objCampaign.NextRunAt is DateTime dtOccurrence
                    && dtOccurrence <= dtNowUtc; iGuard++)
                {
                    DateOnly dtDay = CompanyTimeZone.Today(objZone, dtOccurrence);
                    bool bMissed = dtDay < dtToday;
                    // Uma execução por campanha por dia (D1): mudar o horário para mais tarde no mesmo
                    // dia, depois de já ter disparado, não dispara de novo.
                    bool bAlreadyExists = await _objDbContext.CampaignRuns.AnyAsync(
                        run => run.IDCampaign == objCampaign.Id && run.LocalDate == dtDay,
                        objCancellationToken);
                    if (!bAlreadyExists)
                    {
                        _objDbContext.CampaignRuns.Add(new CampaignRun
                        {
                            IDCampaign = objCampaign.Id,
                            IDCompany = objCampaign.IDCompany,
                            IDCampaignVersion = objVersion.Id,
                            VersionNumber = objVersion.Number,
                            ScheduledFor = dtOccurrence,
                            LocalDate = dtDay,
                            Status = bMissed ? CampaignRunStatus.Missed : CampaignRunStatus.Selecting,
                            Simulation = false,
                            CreatedAt = dtNowUtc,
                            FinishedAt = bMissed ? dtNowUtc : null,
                            Error = bMissed ? "O serviço estava fora do ar no horário e o dia já virou." : null,
                        });
                        iCreated++;
                    }
                    if (!bMissed)
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
            return iCreated;
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
            HashSet<Guid> objAlreadyInRun = (await _objDbContext.CampaignRecipients.AsNoTracking()
                    .Where(recipient => recipient.IDRun == objRunId)
                    .Select(recipient => recipient.IDCustomer)
                    .ToListAsync(objCancellationToken))
                .ToHashSet();

            // D11: no máximo uma mensagem por cliente por dia, somando as campanhas da empresa.
            var objOtherToday = await (
                from recipient in _objDbContext.CampaignRecipients.AsNoTracking()
                join run in _objDbContext.CampaignRuns.AsNoTracking() on recipient.IDRun equals run.Id
                join campaign in _objDbContext.Campaigns.AsNoTracking() on run.IDCampaign equals campaign.Id
                where run.IDCompany == objRun.IDCompany && run.LocalDate == objRun.LocalDate
                    && run.Id != objRunId && s_arrDelivered.Contains(recipient.Status)
                select new { recipient.Id, recipient.IDCustomer, recipient.Status, RunId = run.Id, campaign.Kind, campaign.Name })
                .ToListAsync(objCancellationToken);
            var objOtherByCustomer = objOtherToday
                .GroupBy(item => item.IDCustomer)
                .ToDictionary(group => group.Key, group => group.First());

            int iPriority = CampaignKind.Priority(objCampaign.Kind);
            int? iMilestone = objCampaign.Kind == CampaignKind.FrequentCustomer ? objConfig.VisitMilestone ?? 5 : null;
            int iBatch = Math.Max(1, _objOptions.SelectionChunkSize);

            List<CampaignRecipient> objBatch = new(iBatch);
            int iIgnoredInBatch = 0;
            foreach (AudienceMember objMember in objAudience)
            {
                if (objAlreadyInRun.Contains(objMember.Id))
                {
                    continue;
                }

                int? iCustomerMilestone = iMilestone is int iStep ? objMember.VisitCount / iStep * iStep : null;
                (string sInfo, DateOnly? dtEvent, int? iAge) = Detail(
                    objCampaign.Kind, objMember, objRun.LocalDate, iCustomerMilestone);
                string sMessage = CampaignMessage.Render(objConfig.Message, new CampaignMessageData(
                    objMember.Name,
                    objCompany.Name,
                    objMember.IDLastUnit is Guid objUnitId ? objUnitNames.GetValueOrDefault(objUnitId, "") : "",
                    iAge,
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
                    Message = sMessage.Length <= 4000 ? sMessage : sMessage[..4000],
                    Info = sInfo,
                    EventDate = dtEvent,
                    Status = CampaignRecipientStatus.Pending,
                    Milestone = iCustomerMilestone,
                    CreatedAt = dtNowUtc,
                };

                if (objOtherByCustomer.TryGetValue(objMember.Id, out var objOther))
                {
                    bool bOtherLessImportant = CampaignKind.Priority(objOther.Kind) > iPriority;
                    if (objOther.Status == CampaignRecipientStatus.Pending && bOtherLessImportant)
                    {
                        // A outra ainda não saiu e é menos importante: esta ocupa o lugar dela.
                        await ReplaceAsync(objOther.Id, objOther.RunId, objCampaign.Name, dtNowUtc, objCancellationToken);
                    }
                    else
                    {
                        objRecipient.Status = CampaignRecipientStatus.Ignored;
                        objRecipient.Reason = $"Limite do dia: já recebe \"{objOther.Name}\" hoje.";
                        objRecipient.ProcessedAt = dtNowUtc;
                        iIgnoredInBatch++;
                    }
                }

                objBatch.Add(objRecipient);
                if (objBatch.Count >= iBatch)
                {
                    if (!await SaveBatchAsync(objRunId, objBatch, iIgnoredInBatch, objCancellationToken))
                    {
                        return; // cancelada no meio da seleção
                    }
                    iIgnoredInBatch = 0;
                }
            }

            if (!await SaveBatchAsync(objRunId, objBatch, iIgnoredInBatch, objCancellationToken))
            {
                return;
            }

            objRun = await _objDbContext.CampaignRuns.FirstAsync(run => run.Id == objRunId, objCancellationToken);
            if (objRun.Status == CampaignRunStatus.Selecting)
            {
                bool bHasPending = objRun.TotalCount > objRun.IgnoredCount;
                objRun.Status = bHasPending ? CampaignRunStatus.Running : CampaignRunStatus.Completed;
                objRun.FinishedAt = bHasPending ? null : dtNowUtc;
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
        private static (string Info, DateOnly? EventDate, int? Age) Detail(
            string sKind, AudienceMember objMember, DateOnly dtDay, int? iMilestone)
        {
            int? iAge = CampaignMessage.AgeOn(objMember.BirthDate, dtDay);
            switch (sKind)
            {
                case CampaignKind.Birthday when objMember.BirthDate is DateOnly dtBirth:
                    DateOnly dtBirthday = CampaignCalendar.BirthdayInRange(dtBirth, dtDay) ?? dtDay;
                    int iYears = CampaignMessage.FullYearsBetween(dtBirth, dtBirthday);
                    return ($"{CampaignPdf.ShortDate(dtBirthday)} · {iYears} anos", dtBirthday, iYears);
                case CampaignKind.SignupAnniversary:
                    int iSignup = CampaignMessage.FullYearsBetween(objMember.FirstVisitDate, dtDay);
                    return (iSignup == 1 ? "1 ano de cadastro" : $"{iSignup} anos de cadastro", null, iAge);
                case CampaignKind.FrequentCustomer when iMilestone is int iVisits:
                    return ($"{iVisits}ª visita", null, iAge);
                case CampaignKind.WeMissYou:
                    return ($"Última visita em {objMember.LastVisitDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}",
                        null, iAge);
                default:
                    return ("", null, iAge);
            }
        }

        /// <summary>Grava um bloco da seleção e soma no total. Devolve false se a execução foi cancelada.</summary>
        private async Task<bool> SaveBatchAsync(
            Guid objRunId, List<CampaignRecipient> objBatch, int iIgnored, CancellationToken objCancellationToken)
        {
            CampaignRun objRun = await _objDbContext.CampaignRuns.FirstAsync(run => run.Id == objRunId, objCancellationToken);
            if (objRun.Status != CampaignRunStatus.Selecting)
            {
                return false;
            }

            _objDbContext.CampaignRecipients.AddRange(objBatch);
            objRun.TotalCount += objBatch.Count;
            objRun.IgnoredCount += iIgnored;
            // Salva mesmo com o bloco vazio: leva junto as substituições feitas em outras execuções.
            await _objDbContext.SaveChangesAsync(objCancellationToken);

            objBatch.Clear();
            // Sem isso o rastreamento cresce a cada bloco e uma seleção grande fica cada vez mais lenta.
            _objDbContext.ChangeTracker.Clear();
            return true;
        }

        private async Task ReplaceAsync(
            long lRecipientId, Guid objOtherRunId, string sCampaign, DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            CampaignRecipient? objOther = await _objDbContext.CampaignRecipients
                .FirstOrDefaultAsync(recipient => recipient.Id == lRecipientId, objCancellationToken);
            if (objOther is null || objOther.Status != CampaignRecipientStatus.Pending)
            {
                return;
            }
            objOther.Status = CampaignRecipientStatus.Ignored;
            objOther.Reason = $"Substituída por \"{sCampaign}\" (limite de 1 mensagem por dia).";
            objOther.ProcessedAt = dtNowUtc;

            CampaignRun objOtherRun = await _objDbContext.CampaignRuns
                .FirstAsync(run => run.Id == objOtherRunId, objCancellationToken);
            objOtherRun.IgnoredCount++;
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
            await CreateDeliveriesAsync(objRun, objCampaign, dtNowUtc, objCancellationToken);

            List<CampaignDelivery> objExpired = await _objDbContext.CampaignDeliveries
                .Where(delivery => delivery.IDRun == objRunId && delivery.Status == CampaignDeliveryStatus.Pending
                    && (delivery.NextAttemptAt == null || delivery.NextAttemptAt <= dtNowUtc))
                .OrderBy(delivery => delivery.UnitName)
                .ToListAsync(objCancellationToken);

            int iCustomers = 0;
            foreach (CampaignDelivery objDelivery in objExpired)
            {
                // Pausada ou cancelada pela tela no meio dos envios: para aqui.
                string sStatus = await _objDbContext.CampaignRuns.AsNoTracking()
                    .Where(run => run.Id == objRunId).Select(run => run.Status).FirstAsync(objCancellationToken);
                if (sStatus != CampaignRunStatus.Running)
                {
                    return iCustomers;
                }
                iCustomers += await SendDeliveriesAsync(objRun, objCampaign, objDelivery, dtNowUtc, objCancellationToken);
            }

            bool bHasPending = await _objDbContext.CampaignRecipients.AnyAsync(
                recipient => recipient.IDRun == objRunId && recipient.Status == CampaignRecipientStatus.Pending,
                objCancellationToken);
            string sNow = await _objDbContext.CampaignRuns.AsNoTracking()
                .Where(run => run.Id == objRunId).Select(run => run.Status).FirstAsync(objCancellationToken);
            if (!bHasPending && sNow == CampaignRunStatus.Running)
            {
                objRun.Status = CampaignRunStatus.Completed;
                objRun.FinishedAt = dtNowUtc;
                await _objDbContext.SaveChangesAsync(objCancellationToken);
            }
            return iCustomers;
        }

        /// <summary>
        /// Uma entrega por unidade que tem cliente pendente. Unidade sem e-mail (ou cliente sem unidade):
        /// a entrega já nasce como falha, com o motivo, e os clientes dela também.
        /// </summary>
        private async Task CreateDeliveriesAsync(
            CampaignRun objRun, Campaign objCampaign, DateTime dtNowUtc, CancellationToken objCancellationToken)
        {
            var objGroups = await _objDbContext.CampaignRecipients.AsNoTracking()
                .Where(recipient => recipient.IDRun == objRun.Id && recipient.Status == CampaignRecipientStatus.Pending)
                .GroupBy(recipient => recipient.IDUnit)
                .Select(group => new { IDUnit = group.Key, Total = group.Count() })
                .ToListAsync(objCancellationToken);
            if (objGroups.Count == 0)
            {
                return;
            }

            List<Guid?> objAlreadyHas = await _objDbContext.CampaignDeliveries.AsNoTracking()
                .Where(delivery => delivery.IDRun == objRun.Id)
                .Select(delivery => delivery.IDUnit)
                .ToListAsync(objCancellationToken);
            Dictionary<Guid, Unit> objUnits = await _objDbContext.Units.AsNoTracking()
                .Where(unit => unit.IDCompany == objRun.IDCompany)
                .ToDictionaryAsync(unit => unit.Id, objCancellationToken);

            foreach (var objGroup in objGroups.Where(group => !objAlreadyHas.Contains(group.IDUnit)))
            {
                Unit? objUnit = objGroup.IDUnit is Guid objUnitId ? objUnits.GetValueOrDefault(objUnitId) : null;
                string sEmail = objUnit?.Email.Trim() ?? "";
                CampaignDelivery objDelivery = new CampaignDelivery
                {
                    IDRun = objRun.Id,
                    IDUnit = objGroup.IDUnit,
                    UnitName = objUnit?.Name ?? "Sem unidade",
                    Email = sEmail,
                    RecipientCount = objGroup.Total,
                    FileName = CampaignDeliveryDocument.FileName(objCampaign.Kind, objUnit?.Slug ?? "", objRun.LocalDate),
                    CreatedAt = dtNowUtc,
                    NextAttemptAt = dtNowUtc,
                };

                string? sNoDestination = objUnit is null
                    ? "Clientes sem unidade: não há para quem mandar."
                    : sEmail.Length == 0
                        ? $"A unidade {objUnit.Name} não tem e-mail cadastrado (Unidades → editar → e-mail)."
                        : null;
                if (sNoDestination is not null)
                {
                    objDelivery.Status = CampaignDeliveryStatus.Failed;
                    objDelivery.Error = sNoDestination;
                    objDelivery.NextAttemptAt = null;
                    objRun.FailedCount += await MarkCustomersAsync(
                        objRun.Id, objGroup.IDUnit, CampaignRecipientStatus.Failed, sNoDestination, dtNowUtc, objCancellationToken);
                    _objLogger.LogWarning("Campanha {Campanha}: {Motivo}", objCampaign.Name, sNoDestination);
                }
                _objDbContext.CampaignDeliveries.Add(objDelivery);
            }

            await _objDbContext.SaveChangesAsync(objCancellationToken);
        }

        /// <summary>Monta o PDF da unidade e manda o e-mail. Devolve quantos clientes foram (0 se falhou).</summary>
        private async Task<int> SendDeliveriesAsync(
            CampaignRun objRun, Campaign objCampaign, CampaignDelivery objDelivery, DateTime dtNowUtc,
            CancellationToken objCancellationToken)
        {
            CampaignPdfData objData = await CampaignDeliveryDocument.LoadAsync(
                _objDbContext, objRun, objDelivery.IDUnit, s_arrPending, objCancellationToken);
            if (objData.Rows.Count == 0)
            {
                // Os clientes saíram desta execução antes do envio (ex.: outra campanha ocupou o dia).
                objDelivery.Status = CampaignDeliveryStatus.Cancelled;
                objDelivery.RecipientCount = 0;
                objDelivery.NextAttemptAt = null;
                await _objDbContext.SaveChangesAsync(objCancellationToken);
                return 0;
            }

            string? sError = null;
            string sSubject = Subject(objData);
            string sBody = Body(objData);
            try
            {
                byte[] arrPdf = CampaignPdf.Build(objData);
                await _objEmailSender.SendAsync(
                    objDelivery.Email, sSubject, sBody, arrPdf, objDelivery.FileName, objCancellationToken);
            }
            catch (Exception objException) when (objException is not OperationCanceledException)
            {
                sError = objException.Message;
            }

            objDelivery.Attempts++;
            objDelivery.RecipientCount = objData.Rows.Count;
            if (sError is null)
            {
                objDelivery.Status = CampaignDeliveryStatus.Sent;
                objDelivery.SentAt = dtNowUtc;
                objDelivery.Error = null;
                objDelivery.NextAttemptAt = null;
                objRun.SentCount += await MarkCustomersAsync(
                    objRun.Id, objDelivery.IDUnit, CampaignRecipientStatus.Sent, null, dtNowUtc, objCancellationToken);
                // Correio eletrônico: o e-mail como saiu (o PDF é remontado da execução, se pedirem).
                _objDbContext.SentEmails.Add(new SentEmail
                {
                    IDCompany = objRun.IDCompany,
                    IDUnit = objDelivery.IDUnit,
                    UnitName = objDelivery.UnitName,
                    Kind = SentEmailKind.Campaign,
                    ToEmail = objDelivery.Email,
                    Subject = sSubject,
                    Body = sBody,
                    AttachmentName = objDelivery.FileName,
                    SentAt = dtNowUtc,
                    IDCampaignRun = objRun.Id,
                });
                await _objDbContext.SaveChangesAsync(objCancellationToken);
                _objLogger.LogInformation(
                    "Campanha {Campanha}: PDF com {Total} cliente(s) enviado para {Email} (unidade {Unidade}).",
                    objCampaign.Name, objData.Rows.Count, objDelivery.Email, objDelivery.UnitName);
                return objData.Rows.Count;
            }

            objDelivery.Error = sError.Length <= 500 ? sError : sError[..500];
            int iMax = Math.Max(1, _objOptions.EmailMaxAttempts);
            if (objDelivery.Attempts >= iMax)
            {
                objDelivery.Status = CampaignDeliveryStatus.Failed;
                objDelivery.NextAttemptAt = null;
                string sReason = $"O e-mail para {objDelivery.Email} não saiu ({objDelivery.Attempts} tentativas): {sError}";
                objRun.FailedCount += await MarkCustomersAsync(
                    objRun.Id, objDelivery.IDUnit, CampaignRecipientStatus.Failed, sReason, dtNowUtc, objCancellationToken);
                _objLogger.LogError(
                    "Campanha {Campanha}: e-mail da unidade {Unidade} para {Email} desistido após {Tentativas} tentativas: {Erro}",
                    objCampaign.Name, objDelivery.UnitName, objDelivery.Email, objDelivery.Attempts, sError);
            }
            else
            {
                objDelivery.NextAttemptAt = dtNowUtc.AddMinutes(Math.Max(1, _objOptions.EmailRetryMinutes));
                _objLogger.LogWarning(
                    "Campanha {Campanha}: e-mail da unidade {Unidade} para {Email} falhou (tentativa {Tentativa} de {Maximo}), tenta de novo às {Proxima:HH:mm} UTC: {Erro}",
                    objCampaign.Name, objDelivery.UnitName, objDelivery.Email, objDelivery.Attempts, iMax,
                    objDelivery.NextAttemptAt, sError);
            }
            await _objDbContext.SaveChangesAsync(objCancellationToken);
            return 0;
        }

        /// <summary>Muda o status dos clientes pendentes de uma unidade na execução. Devolve quantos.</summary>
        private async Task<int> MarkCustomersAsync(
            Guid objRunId, Guid? objUnitId, string sStatus, string? sReason, DateTime dtNowUtc,
            CancellationToken objCancellationToken)
        {
            List<CampaignRecipient> objCustomers = await _objDbContext.CampaignRecipients
                .Where(recipient => recipient.IDRun == objRunId && recipient.IDUnit == objUnitId
                    && recipient.Status == CampaignRecipientStatus.Pending)
                .ToListAsync(objCancellationToken);
            string? sShort = sReason is { Length: > 300 } ? sReason[..300] : sReason;
            foreach (CampaignRecipient objCustomer in objCustomers)
            {
                objCustomer.Status = sStatus;
                objCustomer.Reason = sShort;
                objCustomer.ProcessedAt = dtNowUtc;
            }
            return objCustomers.Count;
        }

        private static string Subject(CampaignPdfData objData) =>
            $"Campanha {objData.CampaignName} — {objData.UnitName} — " +
            $"{objData.LocalDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} ({CustomersText(objData.Rows.Count)})";

        private static string Body(CampaignPdfData objData)
        {
            StringBuilder objBody = new StringBuilder();
            objBody.Append($"Olá, equipe da unidade {objData.UnitName}!\r\n\r\n");
            objBody.Append($"Segue em anexo o PDF da campanha \"{objData.CampaignName}\" com {CustomersText(objData.Rows.Count)} ");
            objBody.Append($"para vocês entrarem em contato hoje ({CampaignPdf.LongDate(objData.LocalDate)}).\r\n");
            if (objData.Kind == CampaignKind.Birthday)
            {
                (DateOnly dtStart, DateOnly dtEnd) = CampaignCalendar.BirthdayRange(objData.LocalDate);
                objBody.Append($"São os aniversariantes de {CampaignPdf.ShortDate(dtStart)} a {CampaignPdf.ShortDate(dtEnd)}.\r\n");
            }
            objBody.Append("\r\nMensagem para enviar:\r\n");
            objBody.Append(CampaignMessage.RenderShared(objData.MessageTemplate, objData.CompanyName, objData.UnitName)
                .Replace("\r\n", "\n").Replace("\n", "\r\n"));
            objBody.Append("\r\n\r\nNo PDF, clique no WhatsApp de cada cliente: a conversa abre com a mensagem pronta, ");
            objBody.Append("já com o nome dele. É só conferir e enviar.\r\n\r\n");
            objBody.Append("Mensagem automática do AccessWifi.");
            return objBody.ToString();
        }

        private static string CustomersText(int iTotal) => iTotal == 1 ? "1 cliente" : $"{iTotal} clientes";

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
                    List<CampaignRecipient> objBatch = await _objDbContext.CampaignRecipients
                        .Where(recipient => recipient.IDRun == objRunId && recipient.Status == CampaignRecipientStatus.Pending)
                        .OrderBy(recipient => recipient.Id)
                        .Take(1000)
                        .ToListAsync(objCancellationToken);
                    if (objBatch.Count == 0)
                    {
                        break;
                    }

                    foreach (CampaignRecipient objRecipient in objBatch)
                    {
                        objRecipient.Status = CampaignRecipientStatus.Cancelled;
                        objRecipient.Reason = "Execução cancelada.";
                        objRecipient.ProcessedAt = dtNowUtc;
                    }
                    CampaignRun objRun = await _objDbContext.CampaignRuns.FirstAsync(run => run.Id == objRunId, objCancellationToken);
                    objRun.CancelledCount += objBatch.Count;
                    objRun.FinishedAt ??= dtNowUtc;
                    await _objDbContext.SaveChangesAsync(objCancellationToken);
                }

                List<CampaignDelivery> objNotSent = await _objDbContext.CampaignDeliveries
                    .Where(delivery => delivery.IDRun == objRunId && delivery.Status == CampaignDeliveryStatus.Pending)
                    .ToListAsync(objCancellationToken);
                foreach (CampaignDelivery objDelivery in objNotSent)
                {
                    objDelivery.Status = CampaignDeliveryStatus.Cancelled;
                    objDelivery.NextAttemptAt = null;
                }
                await _objDbContext.SaveChangesAsync(objCancellationToken);
            }
        }
    }
}
