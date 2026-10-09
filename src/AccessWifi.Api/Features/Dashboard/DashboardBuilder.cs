using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Features.Dashboard;

/// <summary>
/// Calcula os números do dashboard (PROPOSTA_DASHBOARD.md §3) para um conjunto de unidades da empresa.
///
/// De onde sai cada número:
/// <list type="bullet">
///   <item>conexões, horário e dia da semana, retorno e pontos de acesso: tabela <see cref="Visit"/>, que só
///   existe desde 08/10/2026 (<see cref="DashboardDto.VisitsSince"/>);</item>
///   <item>clientes novos e base de clientes: 1ª visita do cliente nas unidades da visão
///   (<see cref="CustomerUnit"/>), com histórico desde o início;</item>
///   <item>faixa etária, frequência, aniversariantes e Instagram: <see cref="Customer"/>.</item>
/// </list>
/// "Voltou" = visita num dia depois do dia da 1ª visita do cliente às unidades da visão (no fuso da empresa):
/// voltar no mesmo dia não conta, e quem chegou no período e voltou dias depois conta.
/// </summary>
public class DashboardBuilder
{
    private static readonly string[] s_arrBands = ["Menos de 18", "18 a 24", "25 a 34", "35 a 44", "45 a 59", "60 ou mais"];
    private static readonly string[] s_arrFrequencies = ["1 visita", "2 visitas", "3 a 4 visitas", "5 ou mais"];

    private readonly AppDbContext _objDbContext;

    public DashboardBuilder(AppDbContext objDbContext)
    {
        _objDbContext = objDbContext;
    }

    /// <summary>Conexão do período, só com o que as contas usam.</summary>
    private record LoadedVisit(Guid IDUnit, Guid? IDCustomer, DateOnly LocalDate, int LocalHour, string Ap);

    /// <summary>Contas de um período (o pedido e o anterior usam as mesmas).</summary>
    private sealed class Period
    {
        public List<LoadedVisit> Visits { get; init; } = [];
        public int NewCustomers { get; init; }
        public Dictionary<DateOnly, int> NewByDay { get; init; } = [];
        public HashSet<Guid> Visitors { get; init; } = [];
        public HashSet<Guid> Returning { get; init; } = [];
        public Dictionary<DateOnly, int> ReturningByDay { get; init; } = [];
        public Dictionary<Guid, (int Visitors, int Returning)> ByUnit { get; init; } = [];
    }

    /// <param name="objUnits">Unidades da visão: todas as que o usuário pode ver, ou só a escolhida.</param>
    /// <param name="bUnitView">Visão de uma unidade (pontos de acesso) em vez da visão empresa (tabela por unidade).</param>
    public async Task<DashboardDto> BuildAsync(
        Company objCompany, List<Unit> objUnits, bool bUnitView, DateOnly dtFrom, DateOnly dtTo,
        DateTime dtNowUtc, CancellationToken objCancellationToken = default)
    {
        TimeZoneInfo objZone = CompanyTimeZone.Resolve(objCompany.TimeZone);
        DateOnly dtToday = CompanyTimeZone.Today(objZone, dtNowUtc);
        Guid[] arrUnits = objUnits.Select(unit => unit.Id).ToArray();
        int iDays = dtTo.DayNumber - dtFrom.DayNumber + 1;
        DateOnly dtPrevTo = dtFrom.AddDays(-1);
        DateOnly dtPrevFrom = dtPrevTo.AddDays(-(iDays - 1));
        (DateTime dtStartUtc, DateTime dtEndUtc) = Bounds(dtFrom, dtTo, objZone);
        DateTime dt30Utc = dtNowUtc.AddDays(-30);

        Period objCurrent = await ComputePeriodAsync(arrUnits, dtFrom, dtTo, objZone, objCancellationToken);
        Period objPrevious = await ComputePeriodAsync(arrUnits, dtPrevFrom, dtPrevTo, objZone, objCancellationToken);

        // Clientes da visão: os que já visitaram alguma das unidades dela.
        IQueryable<CustomerUnit> objLinks = _objDbContext.CustomerUnits.AsNoTracking()
            .Where(link => arrUnits.Contains(link.IDUnit));
        IQueryable<Customer> objCustomers = _objDbContext.Customers.AsNoTracking()
            .Where(customer => customer.IDCompany == objCompany.Id
                && _objDbContext.CustomerUnits.Any(link => link.IDCustomer == customer.Id && arrUnits.Contains(link.IDUnit)));

        int iBase = await objLinks
            .Where(link => link.FirstVisitAt < dtEndUtc)
            .Select(link => link.IDCustomer)
            .Distinct()
            .CountAsync(objCancellationToken);
        int iActive = await objLinks
            .Where(link => link.LastVisitAt >= dt30Utc)
            .Select(link => link.IDCustomer)
            .Distinct()
            .CountAsync(objCancellationToken);

        DashboardKpisDto objKpis = new DashboardKpisDto(
            new DashboardCompareDto(objCurrent.Visits.Count, objPrevious.Visits.Count),
            new DashboardCompareDto(objCurrent.NewCustomers, objPrevious.NewCustomers),
            new DashboardCompareDto(objCurrent.Visitors.Count, objPrevious.Visitors.Count),
            new DashboardCompareDto(objCurrent.Returning.Count, objPrevious.Returning.Count),
            new DashboardRateDto(
                Rate(objCurrent.Returning.Count, objCurrent.Visitors.Count),
                Rate(objPrevious.Returning.Count, objPrevious.Visitors.Count)),
            iBase,
            iActive);

        Dictionary<DateOnly, int> objVisitsByDay = objCurrent.Visits
            .GroupBy(visit => visit.LocalDate)
            .ToDictionary(group => group.Key, group => group.Count());
        List<DashboardDayDto> objDaily = Enumerable.Range(0, iDays)
            .Select(iDay => dtFrom.AddDays(iDay))
            .Select(dtDay => new DashboardDayDto(
                dtDay,
                objVisitsByDay.GetValueOrDefault(dtDay),
                objCurrent.NewByDay.GetValueOrDefault(dtDay),
                objCurrent.ReturningByDay.GetValueOrDefault(dtDay)))
            .ToList();

        List<int> objHourly = Enumerable.Range(0, 24)
            .Select(iHour => objCurrent.Visits.Count(visit => visit.LocalHour == iHour))
            .ToList();
        List<int> objWeekday = Enumerable.Range(0, 7)
            .Select(iDay => objCurrent.Visits.Count(visit => (int)visit.LocalDate.DayOfWeek == iDay))
            .ToList();

        List<DashboardBucketDto> objBands = await AgeBandsAsync(objCustomers, dtToday, objCancellationToken);
        List<DashboardBucketDto> objFrequency = await FrequencyAsync(objCustomers, objCancellationToken);

        List<DashboardUnitRowDto> objRows = bUnitView
            ? []
            : await RowsByUnitAsync(objUnits, objCurrent, dtStartUtc, dtEndUtc, dt30Utc, objCancellationToken);
        List<DashboardApDto> objAps = bUnitView && objUnits.Count == 1
            ? await AccessPointsAsync(objUnits[0].Id, objCurrent, objCancellationToken)
            : [];

        int iBirthdays = await objCustomers
            .CountAsync(customer => customer.BirthDate != null && customer.BirthDate.Value.Month == dtToday.Month, objCancellationToken);
        int iWithInstagram = await objCustomers.CountAsync(customer => customer.Instagram != "", objCancellationToken);
        (int iRuns, int iSent, int iFailures) = await CampaignsAsync(arrUnits, dtFrom, dtTo, objCancellationToken);

        DateOnly? dtSince = await _objDbContext.Visits.AsNoTracking()
            .Where(visit => arrUnits.Contains(visit.IDUnit))
            .Select(visit => (DateOnly?)visit.LocalDate)
            .MinAsync(objCancellationToken);

        Unit? objSingle = bUnitView && objUnits.Count == 1 ? objUnits[0] : null;
        return new DashboardDto(
            new DashboardRefDto(objCompany.Id, objCompany.Slug, objCompany.Name),
            objSingle is null ? null : new DashboardRefDto(objSingle.Id, objSingle.Slug, objSingle.Name),
            new DashboardPeriodDto(dtFrom, dtTo, dtPrevFrom, dtPrevTo, iDays),
            dtSince,
            objKpis,
            objDaily,
            objHourly,
            objWeekday,
            objBands,
            objFrequency,
            objRows,
            objAps,
            new DashboardExtrasDto(iBirthdays, iWithInstagram, iRuns, iSent, iFailures));
    }

    private async Task<Period> ComputePeriodAsync(
        Guid[] arrUnits, DateOnly dtFrom, DateOnly dtTo, TimeZoneInfo objZone, CancellationToken objCancellationToken)
    {
        (DateTime dtStartUtc, DateTime dtEndUtc) = Bounds(dtFrom, dtTo, objZone);

        List<LoadedVisit> objVisits = await _objDbContext.Visits.AsNoTracking()
            .Where(visit => arrUnits.Contains(visit.IDUnit) && visit.LocalDate >= dtFrom && visit.LocalDate <= dtTo)
            .Select(visit => new LoadedVisit(visit.IDUnit, visit.IDCustomer, visit.LocalDate, visit.LocalHour, visit.Ap))
            .ToListAsync(objCancellationToken);

        // Clientes novos: a 1ª visita a alguma unidade da visão caiu no período.
        List<DateTime> objFirstsInPeriod = await _objDbContext.CustomerUnits.AsNoTracking()
            .Where(link => arrUnits.Contains(link.IDUnit))
            .GroupBy(link => link.IDCustomer)
            .Select(group => group.Min(link => link.FirstVisitAt))
            .Where(dtFirst => dtFirst >= dtStartUtc && dtFirst < dtEndUtc)
            .ToListAsync(objCancellationToken);
        Dictionary<DateOnly, int> objNewByDay = objFirstsInPeriod
            .GroupBy(dtFirst => CompanyTimeZone.Today(objZone, dtFirst))
            .ToDictionary(group => group.Key, group => group.Count());

        // Voltou: visita num dia depois do dia da 1ª visita (às unidades da visão e a cada unidade).
        Guid[] arrCustomers = objVisits
            .Where(visit => visit.IDCustomer is not null)
            .Select(visit => visit.IDCustomer!.Value)
            .Distinct()
            .ToArray();
        List<CustomerUnit> objFirsts = arrCustomers.Length == 0
            ? []
            : await _objDbContext.CustomerUnits.AsNoTracking()
                .Where(link => arrCustomers.Contains(link.IDCustomer) && arrUnits.Contains(link.IDUnit))
                .ToListAsync(objCancellationToken);
        Dictionary<Guid, DateOnly> objFirstDay = objFirsts
            .GroupBy(link => link.IDCustomer)
            .ToDictionary(group => group.Key, group => CompanyTimeZone.Today(objZone, group.Min(link => link.FirstVisitAt)));
        Dictionary<(Guid, Guid), DateOnly> objFirstDayInUnit = objFirsts
            .ToDictionary(link => (link.IDCustomer, link.IDUnit), link => CompanyTimeZone.Today(objZone, link.FirstVisitAt));

        List<LoadedVisit> objWithCustomer = objVisits.Where(visit => visit.IDCustomer is not null).ToList();
        List<LoadedVisit> objReturns = objWithCustomer
            .Where(visit => objFirstDay.TryGetValue(visit.IDCustomer!.Value, out DateOnly dtFirst)
                && visit.LocalDate > dtFirst)
            .ToList();

        Dictionary<Guid, (int, int)> objByUnit = objWithCustomer
            .GroupBy(visit => visit.IDUnit)
            .ToDictionary(
                group => group.Key,
                group => (
                    group.Select(visit => visit.IDCustomer!.Value).Distinct().Count(),
                    group.Where(visit => objFirstDayInUnit.TryGetValue(
                            (visit.IDCustomer!.Value, visit.IDUnit), out DateOnly dtFirst)
                            && visit.LocalDate > dtFirst)
                        .Select(visit => visit.IDCustomer!.Value)
                        .Distinct()
                        .Count()));

        return new Period
        {
            Visits = objVisits,
            NewCustomers = objFirstsInPeriod.Count,
            NewByDay = objNewByDay,
            Visitors = objWithCustomer.Select(visit => visit.IDCustomer!.Value).ToHashSet(),
            Returning = objReturns.Select(visit => visit.IDCustomer!.Value).ToHashSet(),
            ReturningByDay = objReturns
                .GroupBy(visit => visit.LocalDate)
                .ToDictionary(group => group.Key, group => group.Select(visit => visit.IDCustomer).Distinct().Count()),
            ByUnit = objByUnit,
        };
    }

    private async Task<List<DashboardUnitRowDto>> RowsByUnitAsync(
        List<Unit> objUnits, Period objCurrent, DateTime dtStartUtc, DateTime dtEndUtc, DateTime dt30Utc,
        CancellationToken objCancellationToken)
    {
        Guid[] arrUnits = objUnits.Select(unit => unit.Id).ToArray();
        var objUnitCounts = await _objDbContext.CustomerUnits.AsNoTracking()
            .Where(link => arrUnits.Contains(link.IDUnit))
            .GroupBy(link => link.IDUnit)
            .Select(group => new
            {
                IDUnit = group.Key,
                NewCustomers = group.Count(link => link.FirstVisitAt >= dtStartUtc && link.FirstVisitAt < dtEndUtc),
                Customers = group.Count(link => link.FirstVisitAt < dtEndUtc),
                Active = group.Count(link => link.LastVisitAt >= dt30Utc),
                Last = group.Max(link => (DateTime?)link.LastVisitAt),
            })
            .ToListAsync(objCancellationToken);

        Dictionary<Guid, int> objVisits = objCurrent.Visits
            .GroupBy(visit => visit.IDUnit)
            .ToDictionary(group => group.Key, group => group.Count());

        return objUnits
            .Select(unit =>
            {
                var objUnitCount = objUnitCounts.FirstOrDefault(count => count.IDUnit == unit.Id);
                (int iVisitors, int iReturning) = objCurrent.ByUnit.GetValueOrDefault(unit.Id);
                return new DashboardUnitRowDto(
                    unit.Id, unit.Slug, unit.Name, unit.Active,
                    objVisits.GetValueOrDefault(unit.Id),
                    objUnitCount?.NewCustomers ?? 0,
                    iVisitors,
                    iReturning,
                    Rate(iReturning, iVisitors),
                    objUnitCount?.Customers ?? 0,
                    objUnitCount?.Active ?? 0,
                    objUnitCount?.Last);
            })
            .OrderByDescending(row => row.Connections)
            .ThenBy(row => row.Name)
            .ToList();
    }

    private async Task<List<DashboardApDto>> AccessPointsAsync(
        Guid objUnitId, Period objCurrent, CancellationToken objCancellationToken)
    {
        Dictionary<string, UnitDevice> objDevices = await _objDbContext.UnitDevices.AsNoTracking()
            .Where(device => device.IDUnit == objUnitId)
            .ToDictionaryAsync(device => device.Mac, objCancellationToken);

        return objCurrent.Visits
            .GroupBy(visit => visit.Ap)
            .Select(group =>
            {
                UnitDevice? objDevice = objDevices.GetValueOrDefault(group.Key);
                return new DashboardApDto(
                    group.Key,
                    group.Key.Length == 0 ? "Sem ponto de acesso informado" : objDevice?.Name ?? "Ponto de acesso não identificado",
                    objDevice?.Model ?? "",
                    group.Count());
            })
            .OrderByDescending(ap => ap.Connections)
            .ToList();
    }

    private static async Task<List<DashboardBucketDto>> AgeBandsAsync(
        IQueryable<Customer> objCustomers, DateOnly dtToday, CancellationToken objCancellationToken)
    {
        // Nascido depois de hoje-18 anos = menos de 18, e assim por diante.
        DateOnly dt18 = dtToday.AddYears(-18);
        DateOnly dt25 = dtToday.AddYears(-25);
        DateOnly dt35 = dtToday.AddYears(-35);
        DateOnly dt45 = dtToday.AddYears(-45);
        DateOnly dt60 = dtToday.AddYears(-60);
        Dictionary<int, int> objCounts = await objCustomers
            .Where(customer => customer.BirthDate != null)
            .GroupBy(customer =>
                customer.BirthDate!.Value > dt18 ? 0
                : customer.BirthDate!.Value > dt25 ? 1
                : customer.BirthDate!.Value > dt35 ? 2
                : customer.BirthDate!.Value > dt45 ? 3
                : customer.BirthDate!.Value > dt60 ? 4
                : 5)
            .Select(group => new { group.Key, Total = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Total, objCancellationToken);
        return s_arrBands
            .Select((sBand, iBand) => new DashboardBucketDto(sBand, objCounts.GetValueOrDefault(iBand)))
            .ToList();
    }

    /// <summary>Dias com visita de cada cliente, em todas as lojas da empresa (é o que o cadastro guarda).</summary>
    private static async Task<List<DashboardBucketDto>> FrequencyAsync(
        IQueryable<Customer> objCustomers, CancellationToken objCancellationToken)
    {
        Dictionary<int, int> objCounts = await objCustomers
            .GroupBy(customer => customer.VisitCount >= 5 ? 3 : customer.VisitCount >= 3 ? 2 : customer.VisitCount == 2 ? 1 : 0)
            .Select(group => new { group.Key, Total = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Total, objCancellationToken);
        return s_arrFrequencies
            .Select((sBand, iBand) => new DashboardBucketDto(sBand, objCounts.GetValueOrDefault(iBand)))
            .ToList();
    }

    /// <summary>Execuções no período e mensagens dos clientes das unidades da visão (simulações antigas não contam).</summary>
    private async Task<(int Runs, int Sent, int Failures)> CampaignsAsync(
        Guid[] arrUnits, DateOnly dtFrom, DateOnly dtTo, CancellationToken objCancellationToken)
    {
        var objByStatus = await (
                from recipient in _objDbContext.CampaignRecipients.AsNoTracking()
                join run in _objDbContext.CampaignRuns.AsNoTracking() on recipient.IDRun equals run.Id
                where !run.Simulation
                    && run.LocalDate >= dtFrom && run.LocalDate <= dtTo
                    && recipient.IDUnit != null && arrUnits.Contains(recipient.IDUnit.Value)
                    && (recipient.Status == CampaignRecipientStatus.Sent || recipient.Status == CampaignRecipientStatus.Failed)
                select new { recipient.IDRun, recipient.Status })
            .ToListAsync(objCancellationToken);
        return (
            objByStatus.Select(item => item.IDRun).Distinct().Count(),
            objByStatus.Count(item => item.Status == CampaignRecipientStatus.Sent),
            objByStatus.Count(item => item.Status == CampaignRecipientStatus.Failed));
    }

    /// <summary>Início do 1º dia e início do dia seguinte ao último, no fuso da empresa, em UTC.</summary>
    private static (DateTime StartUtc, DateTime EndUtc) Bounds(DateOnly dtFrom, DateOnly dtTo, TimeZoneInfo objZone) =>
        (TimeZoneInfo.ConvertTimeToUtc(dtFrom.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), objZone),
         TimeZoneInfo.ConvertTimeToUtc(dtTo.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), objZone));

    private static double? Rate(int iPart, int iTotal) =>
        iTotal == 0 ? null : Math.Round(100.0 * iPart / iTotal, 1);
}
