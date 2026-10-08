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
    private static readonly string[] s_arrFaixas = ["Menos de 18", "18 a 24", "25 a 34", "35 a 44", "45 a 59", "60 ou mais"];
    private static readonly string[] s_arrFrequencias = ["1 visita", "2 visitas", "3 a 4 visitas", "5 ou mais"];

    private readonly AppDbContext _objDbContext;

    public DashboardBuilder(AppDbContext objDbContext)
    {
        _objDbContext = objDbContext;
    }

    /// <summary>Conexão do período, só com o que as contas usam.</summary>
    private record ConexaoLida(Guid IDUnit, Guid? IDCustomer, DateOnly LocalDate, int LocalHour, string Ap);

    /// <summary>Contas de um período (o pedido e o anterior usam as mesmas).</summary>
    private sealed class Periodo
    {
        public List<ConexaoLida> Conexoes { get; init; } = [];
        public int Novos { get; init; }
        public Dictionary<DateOnly, int> NovosPorDia { get; init; } = [];
        public HashSet<Guid> Visitantes { get; init; } = [];
        public HashSet<Guid> Voltaram { get; init; } = [];
        public Dictionary<DateOnly, int> VoltaramPorDia { get; init; } = [];
        public Dictionary<Guid, (int Visitantes, int Voltaram)> PorUnidade { get; init; } = [];
    }

    /// <param name="objUnits">Unidades da visão: todas as que o usuário pode ver, ou só a escolhida.</param>
    /// <param name="bVisaoUnidade">Visão de uma unidade (pontos de acesso) em vez da visão empresa (tabela por unidade).</param>
    public async Task<DashboardDto> BuildAsync(
        Company objCompany, List<Unit> objUnits, bool bVisaoUnidade, DateOnly dtFrom, DateOnly dtTo,
        DateTime dtNowUtc, CancellationToken objCancellationToken = default)
    {
        TimeZoneInfo objFuso = CompanyTimeZone.Resolve(objCompany.TimeZone);
        DateOnly dtHoje = CompanyTimeZone.Today(objFuso, dtNowUtc);
        Guid[] arrUnits = objUnits.Select(unit => unit.Id).ToArray();
        int iDias = dtTo.DayNumber - dtFrom.DayNumber + 1;
        DateOnly dtPrevTo = dtFrom.AddDays(-1);
        DateOnly dtPrevFrom = dtPrevTo.AddDays(-(iDias - 1));
        (DateTime dtIniUtc, DateTime dtFimUtc) = Limites(dtFrom, dtTo, objFuso);
        DateTime dt30Utc = dtNowUtc.AddDays(-30);

        Periodo objAtual = await CalcularPeriodoAsync(arrUnits, dtFrom, dtTo, objFuso, objCancellationToken);
        Periodo objAnterior = await CalcularPeriodoAsync(arrUnits, dtPrevFrom, dtPrevTo, objFuso, objCancellationToken);

        // Clientes da visão: os que já visitaram alguma das unidades dela.
        IQueryable<CustomerUnit> objLinks = _objDbContext.CustomerUnits.AsNoTracking()
            .Where(link => arrUnits.Contains(link.IDUnit));
        IQueryable<Customer> objClientes = _objDbContext.Customers.AsNoTracking()
            .Where(customer => customer.IDCompany == objCompany.Id
                && _objDbContext.CustomerUnits.Any(link => link.IDCustomer == customer.Id && arrUnits.Contains(link.IDUnit)));

        int iBase = await objLinks
            .Where(link => link.FirstVisitAt < dtFimUtc)
            .Select(link => link.IDCustomer)
            .Distinct()
            .CountAsync(objCancellationToken);
        int iAtivos = await objLinks
            .Where(link => link.LastVisitAt >= dt30Utc)
            .Select(link => link.IDCustomer)
            .Distinct()
            .CountAsync(objCancellationToken);

        DashboardKpisDto objKpis = new DashboardKpisDto(
            new DashboardCompareDto(objAtual.Conexoes.Count, objAnterior.Conexoes.Count),
            new DashboardCompareDto(objAtual.Novos, objAnterior.Novos),
            new DashboardCompareDto(objAtual.Visitantes.Count, objAnterior.Visitantes.Count),
            new DashboardCompareDto(objAtual.Voltaram.Count, objAnterior.Voltaram.Count),
            new DashboardRateDto(
                Taxa(objAtual.Voltaram.Count, objAtual.Visitantes.Count),
                Taxa(objAnterior.Voltaram.Count, objAnterior.Visitantes.Count)),
            iBase,
            iAtivos);

        Dictionary<DateOnly, int> objConexoesPorDia = objAtual.Conexoes
            .GroupBy(conexao => conexao.LocalDate)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Count());
        List<DashboardDayDto> objDaily = Enumerable.Range(0, iDias)
            .Select(iDia => dtFrom.AddDays(iDia))
            .Select(dtDia => new DashboardDayDto(
                dtDia,
                objConexoesPorDia.GetValueOrDefault(dtDia),
                objAtual.NovosPorDia.GetValueOrDefault(dtDia),
                objAtual.VoltaramPorDia.GetValueOrDefault(dtDia)))
            .ToList();

        List<int> objHourly = Enumerable.Range(0, 24)
            .Select(iHora => objAtual.Conexoes.Count(conexao => conexao.LocalHour == iHora))
            .ToList();
        List<int> objWeekday = Enumerable.Range(0, 7)
            .Select(iDia => objAtual.Conexoes.Count(conexao => (int)conexao.LocalDate.DayOfWeek == iDia))
            .ToList();

        List<DashboardBucketDto> objFaixas = await FaixasEtariasAsync(objClientes, dtHoje, objCancellationToken);
        List<DashboardBucketDto> objFrequencia = await FrequenciaAsync(objClientes, objCancellationToken);

        List<DashboardUnitRowDto> objLinhas = bVisaoUnidade
            ? []
            : await LinhasPorUnidadeAsync(objUnits, objAtual, dtIniUtc, dtFimUtc, dt30Utc, objCancellationToken);
        List<DashboardApDto> objAps = bVisaoUnidade && objUnits.Count == 1
            ? await PontosDeAcessoAsync(objUnits[0].Id, objAtual, objCancellationToken)
            : [];

        int iAniversariantes = await objClientes
            .CountAsync(customer => customer.BirthDate != null && customer.BirthDate.Value.Month == dtHoje.Month, objCancellationToken);
        int iComInstagram = await objClientes.CountAsync(customer => customer.Instagram != "", objCancellationToken);
        (int iExecucoes, int iEnviados, int iFalhas) = await CampanhasAsync(arrUnits, dtFrom, dtTo, objCancellationToken);

        DateOnly? dtDesde = await _objDbContext.Visits.AsNoTracking()
            .Where(visit => arrUnits.Contains(visit.IDUnit))
            .Select(visit => (DateOnly?)visit.LocalDate)
            .MinAsync(objCancellationToken);

        Unit? objUnica = bVisaoUnidade && objUnits.Count == 1 ? objUnits[0] : null;
        return new DashboardDto(
            new DashboardRefDto(objCompany.Id, objCompany.Slug, objCompany.Name),
            objUnica is null ? null : new DashboardRefDto(objUnica.Id, objUnica.Slug, objUnica.Name),
            new DashboardPeriodDto(dtFrom, dtTo, dtPrevFrom, dtPrevTo, iDias),
            dtDesde,
            objKpis,
            objDaily,
            objHourly,
            objWeekday,
            objFaixas,
            objFrequencia,
            objLinhas,
            objAps,
            new DashboardExtrasDto(iAniversariantes, iComInstagram, iExecucoes, iEnviados, iFalhas));
    }

    private async Task<Periodo> CalcularPeriodoAsync(
        Guid[] arrUnits, DateOnly dtFrom, DateOnly dtTo, TimeZoneInfo objFuso, CancellationToken objCancellationToken)
    {
        (DateTime dtIniUtc, DateTime dtFimUtc) = Limites(dtFrom, dtTo, objFuso);

        List<ConexaoLida> objConexoes = await _objDbContext.Visits.AsNoTracking()
            .Where(visit => arrUnits.Contains(visit.IDUnit) && visit.LocalDate >= dtFrom && visit.LocalDate <= dtTo)
            .Select(visit => new ConexaoLida(visit.IDUnit, visit.IDCustomer, visit.LocalDate, visit.LocalHour, visit.Ap))
            .ToListAsync(objCancellationToken);

        // Clientes novos: a 1ª visita a alguma unidade da visão caiu no período.
        List<DateTime> objPrimeirasNoPeriodo = await _objDbContext.CustomerUnits.AsNoTracking()
            .Where(link => arrUnits.Contains(link.IDUnit))
            .GroupBy(link => link.IDCustomer)
            .Select(grupo => grupo.Min(link => link.FirstVisitAt))
            .Where(dtPrimeira => dtPrimeira >= dtIniUtc && dtPrimeira < dtFimUtc)
            .ToListAsync(objCancellationToken);
        Dictionary<DateOnly, int> objNovosPorDia = objPrimeirasNoPeriodo
            .GroupBy(dtPrimeira => CompanyTimeZone.Today(objFuso, dtPrimeira))
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Count());

        // Voltou: visita num dia depois do dia da 1ª visita (às unidades da visão e a cada unidade).
        Guid[] arrClientes = objConexoes
            .Where(conexao => conexao.IDCustomer is not null)
            .Select(conexao => conexao.IDCustomer!.Value)
            .Distinct()
            .ToArray();
        List<CustomerUnit> objPrimeiras = arrClientes.Length == 0
            ? []
            : await _objDbContext.CustomerUnits.AsNoTracking()
                .Where(link => arrClientes.Contains(link.IDCustomer) && arrUnits.Contains(link.IDUnit))
                .ToListAsync(objCancellationToken);
        Dictionary<Guid, DateOnly> objPrimeiroDia = objPrimeiras
            .GroupBy(link => link.IDCustomer)
            .ToDictionary(grupo => grupo.Key, grupo => CompanyTimeZone.Today(objFuso, grupo.Min(link => link.FirstVisitAt)));
        Dictionary<(Guid, Guid), DateOnly> objPrimeiroDiaNaUnidade = objPrimeiras
            .ToDictionary(link => (link.IDCustomer, link.IDUnit), link => CompanyTimeZone.Today(objFuso, link.FirstVisitAt));

        List<ConexaoLida> objComCliente = objConexoes.Where(conexao => conexao.IDCustomer is not null).ToList();
        List<ConexaoLida> objRetornos = objComCliente
            .Where(conexao => objPrimeiroDia.TryGetValue(conexao.IDCustomer!.Value, out DateOnly dtPrimeiro)
                && conexao.LocalDate > dtPrimeiro)
            .ToList();

        Dictionary<Guid, (int, int)> objPorUnidade = objComCliente
            .GroupBy(conexao => conexao.IDUnit)
            .ToDictionary(
                grupo => grupo.Key,
                grupo => (
                    grupo.Select(conexao => conexao.IDCustomer!.Value).Distinct().Count(),
                    grupo.Where(conexao => objPrimeiroDiaNaUnidade.TryGetValue(
                            (conexao.IDCustomer!.Value, conexao.IDUnit), out DateOnly dtPrimeiro)
                            && conexao.LocalDate > dtPrimeiro)
                        .Select(conexao => conexao.IDCustomer!.Value)
                        .Distinct()
                        .Count()));

        return new Periodo
        {
            Conexoes = objConexoes,
            Novos = objPrimeirasNoPeriodo.Count,
            NovosPorDia = objNovosPorDia,
            Visitantes = objComCliente.Select(conexao => conexao.IDCustomer!.Value).ToHashSet(),
            Voltaram = objRetornos.Select(conexao => conexao.IDCustomer!.Value).ToHashSet(),
            VoltaramPorDia = objRetornos
                .GroupBy(conexao => conexao.LocalDate)
                .ToDictionary(grupo => grupo.Key, grupo => grupo.Select(conexao => conexao.IDCustomer).Distinct().Count()),
            PorUnidade = objPorUnidade,
        };
    }

    private async Task<List<DashboardUnitRowDto>> LinhasPorUnidadeAsync(
        List<Unit> objUnits, Periodo objAtual, DateTime dtIniUtc, DateTime dtFimUtc, DateTime dt30Utc,
        CancellationToken objCancellationToken)
    {
        Guid[] arrUnits = objUnits.Select(unit => unit.Id).ToArray();
        var objContas = await _objDbContext.CustomerUnits.AsNoTracking()
            .Where(link => arrUnits.Contains(link.IDUnit))
            .GroupBy(link => link.IDUnit)
            .Select(grupo => new
            {
                IDUnit = grupo.Key,
                Novos = grupo.Count(link => link.FirstVisitAt >= dtIniUtc && link.FirstVisitAt < dtFimUtc),
                Clientes = grupo.Count(link => link.FirstVisitAt < dtFimUtc),
                Ativos = grupo.Count(link => link.LastVisitAt >= dt30Utc),
                Ultima = grupo.Max(link => (DateTime?)link.LastVisitAt),
            })
            .ToListAsync(objCancellationToken);

        Dictionary<Guid, int> objConexoes = objAtual.Conexoes
            .GroupBy(conexao => conexao.IDUnit)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Count());

        return objUnits
            .Select(unit =>
            {
                var objConta = objContas.FirstOrDefault(conta => conta.IDUnit == unit.Id);
                (int iVisitantes, int iVoltaram) = objAtual.PorUnidade.GetValueOrDefault(unit.Id);
                return new DashboardUnitRowDto(
                    unit.Id, unit.Slug, unit.Name, unit.Active,
                    objConexoes.GetValueOrDefault(unit.Id),
                    objConta?.Novos ?? 0,
                    iVisitantes,
                    iVoltaram,
                    Taxa(iVoltaram, iVisitantes),
                    objConta?.Clientes ?? 0,
                    objConta?.Ativos ?? 0,
                    objConta?.Ultima);
            })
            .OrderByDescending(linha => linha.Connections)
            .ThenBy(linha => linha.Name)
            .ToList();
    }

    private async Task<List<DashboardApDto>> PontosDeAcessoAsync(
        Guid objUnitId, Periodo objAtual, CancellationToken objCancellationToken)
    {
        Dictionary<string, UnitDevice> objAparelhos = await _objDbContext.UnitDevices.AsNoTracking()
            .Where(device => device.IDUnit == objUnitId)
            .ToDictionaryAsync(device => device.Mac, objCancellationToken);

        return objAtual.Conexoes
            .GroupBy(conexao => conexao.Ap)
            .Select(grupo =>
            {
                UnitDevice? objAparelho = objAparelhos.GetValueOrDefault(grupo.Key);
                return new DashboardApDto(
                    grupo.Key,
                    grupo.Key.Length == 0 ? "Sem ponto de acesso informado" : objAparelho?.Name ?? "Ponto de acesso não identificado",
                    objAparelho?.Model ?? "",
                    grupo.Count());
            })
            .OrderByDescending(ap => ap.Connections)
            .ToList();
    }

    private static async Task<List<DashboardBucketDto>> FaixasEtariasAsync(
        IQueryable<Customer> objClientes, DateOnly dtHoje, CancellationToken objCancellationToken)
    {
        // Nascido depois de hoje-18 anos = menos de 18, e assim por diante.
        DateOnly dt18 = dtHoje.AddYears(-18);
        DateOnly dt25 = dtHoje.AddYears(-25);
        DateOnly dt35 = dtHoje.AddYears(-35);
        DateOnly dt45 = dtHoje.AddYears(-45);
        DateOnly dt60 = dtHoje.AddYears(-60);
        Dictionary<int, int> objContagem = await objClientes
            .Where(customer => customer.BirthDate != null)
            .GroupBy(customer =>
                customer.BirthDate!.Value > dt18 ? 0
                : customer.BirthDate!.Value > dt25 ? 1
                : customer.BirthDate!.Value > dt35 ? 2
                : customer.BirthDate!.Value > dt45 ? 3
                : customer.BirthDate!.Value > dt60 ? 4
                : 5)
            .Select(grupo => new { grupo.Key, Total = grupo.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Total, objCancellationToken);
        return s_arrFaixas
            .Select((sFaixa, iFaixa) => new DashboardBucketDto(sFaixa, objContagem.GetValueOrDefault(iFaixa)))
            .ToList();
    }

    /// <summary>Dias com visita de cada cliente, em todas as lojas da empresa (é o que o cadastro guarda).</summary>
    private static async Task<List<DashboardBucketDto>> FrequenciaAsync(
        IQueryable<Customer> objClientes, CancellationToken objCancellationToken)
    {
        Dictionary<int, int> objContagem = await objClientes
            .GroupBy(customer => customer.VisitCount >= 5 ? 3 : customer.VisitCount >= 3 ? 2 : customer.VisitCount == 2 ? 1 : 0)
            .Select(grupo => new { grupo.Key, Total = grupo.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Total, objCancellationToken);
        return s_arrFrequencias
            .Select((sFaixa, iFaixa) => new DashboardBucketDto(sFaixa, objContagem.GetValueOrDefault(iFaixa)))
            .ToList();
    }

    /// <summary>Execuções no período e mensagens dos clientes das unidades da visão (simulações antigas não contam).</summary>
    private async Task<(int Execucoes, int Enviados, int Falhas)> CampanhasAsync(
        Guid[] arrUnits, DateOnly dtFrom, DateOnly dtTo, CancellationToken objCancellationToken)
    {
        var objPorStatus = await (
                from recipient in _objDbContext.CampaignRecipients.AsNoTracking()
                join run in _objDbContext.CampaignRuns.AsNoTracking() on recipient.IDRun equals run.Id
                where !run.Simulation
                    && run.LocalDate >= dtFrom && run.LocalDate <= dtTo
                    && recipient.IDUnit != null && arrUnits.Contains(recipient.IDUnit.Value)
                    && (recipient.Status == CampaignRecipientStatus.Sent || recipient.Status == CampaignRecipientStatus.Failed)
                select new { recipient.IDRun, recipient.Status })
            .ToListAsync(objCancellationToken);
        return (
            objPorStatus.Select(item => item.IDRun).Distinct().Count(),
            objPorStatus.Count(item => item.Status == CampaignRecipientStatus.Sent),
            objPorStatus.Count(item => item.Status == CampaignRecipientStatus.Failed));
    }

    /// <summary>Início do 1º dia e início do dia seguinte ao último, no fuso da empresa, em UTC.</summary>
    private static (DateTime IniUtc, DateTime FimUtc) Limites(DateOnly dtFrom, DateOnly dtTo, TimeZoneInfo objFuso) =>
        (TimeZoneInfo.ConvertTimeToUtc(dtFrom.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), objFuso),
         TimeZoneInfo.ConvertTimeToUtc(dtTo.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), objFuso));

    private static double? Taxa(int iParte, int iTotal) =>
        iTotal == 0 ? null : Math.Round(100.0 * iParte / iTotal, 1);
}
