namespace AccessWifi.Api.Features.Dashboard
{
    /// <summary>
    /// Tudo o que a tela do dashboard mostra, numa chamada só (PROPOSTA_DASHBOARD.md). Só números: nenhum
    /// nome ou telefone (D5). Dias e horas no fuso da empresa.
    /// </summary>
    public record DashboardDto(
        DashboardRefDto Company,
        // Null = visão empresa (todas as unidades que o usuário pode ver); preenchido = visão unidade.
        DashboardRefDto? Unit,
        DashboardPeriodDto Period,
        // Primeiro dia com conexão registrada (a tabela de conexões começou em 08/10/2026); null = nenhuma ainda.
        DateOnly? VisitsSince,
        DashboardKpisDto Kpis,
        List<DashboardDayDto> Daily,
        // 24 posições (0h a 23h) e 7 posições (0 = domingo): conexões no período.
        List<int> Hourly,
        List<int> Weekday,
        List<DashboardBucketDto> AgeBands,
        List<DashboardBucketDto> Frequency,
        // Visão empresa: uma linha por unidade. Vazio na visão unidade.
        List<DashboardUnitRowDto> Units,
        // Visão unidade: conexões por ponto de acesso. Vazio na visão empresa.
        List<DashboardApDto> AccessPoints,
        DashboardExtrasDto Extras);

    public record DashboardRefDto(Guid Id, string Slug, string Name);

    /// <summary>Período pedido e o anterior, de mesmo tamanho, usado na comparação (▲▼).</summary>
    public record DashboardPeriodDto(DateOnly From, DateOnly To, DateOnly PreviousFrom, DateOnly PreviousTo, int Days);

    public record DashboardKpisDto(
        DashboardCompareDto Connections,
        DashboardCompareDto NewCustomers,
        // Clientes com visita no período (com telefone que identifica alguém).
        DashboardCompareDto Visitors,
        // Desses, os que já tinham vindo antes (em outro dia) a alguma unidade da visão.
        DashboardCompareDto Returning,
        // Returning ÷ Visitors, em %; null quando não houve visitante.
        DashboardRateDto ReturnRate,
        // Clientes da visão até o fim do período.
        int CustomerBase,
        // Clientes com visita nos últimos 30 dias (contados até agora).
        int Active30d);

    public record DashboardCompareDto(int Current, int Previous);

    public record DashboardRateDto(double? Current, double? Previous);

    public record DashboardDayDto(DateOnly Date, int Connections, int NewCustomers, int Returning);

    public record DashboardBucketDto(string Label, int Count);

    public record DashboardUnitRowDto(
        Guid Id,
        string Slug,
        string Name,
        bool Active,
        int Connections,
        int NewCustomers,
        int Visitors,
        int Returning,
        double? ReturnRate,
        int Customers,
        int Active30d,
        DateTime? LastVisitAt);

    public record DashboardApDto(string Mac, string Name, string Model, int Connections);

    public record DashboardExtrasDto(
        // Clientes da visão que fazem aniversário no mês corrente.
        int BirthdaysThisMonth,
        // Clientes da visão que informaram Instagram.
        int WithInstagram,
        // Campanhas no período, contando só os clientes das unidades da visão.
        int CampaignRuns,
        int CampaignSent,
        int CampaignFailed);
}
