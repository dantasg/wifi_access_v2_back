namespace AccessWifi.Api.Features.Units
{
    /// <summary>
    /// Como estão as visitas pela UniFi numa unidade (PROPOSTA_VISITAS_UNIFI.md). O código do endereço nunca volta
    /// (D1): só se existe e desde quando. "Hoje" é no fuso da empresa.
    /// </summary>
    /// <param name="GuestNetwork">A rede de clientes: o SSID do portal da empresa ("" = não configurado).</param>
    /// <param name="DevicesToday">Aparelhos vistos hoje na rede de clientes (sem os aparelhos da loja).</param>
    /// <param name="VisitsToday">Visitas de hoje; UnifiVisitsToday, as que vieram só pelo aviso.</param>
    public record UnifiVisitsStatusDto(
        bool HasAddress,
        DateTime? AddressCreatedAt,
        DateTime? LastNoticeAt,
        string GuestNetwork,
        int DevicesToday,
        int ConnectsToday,
        int DisconnectsToday,
        int VisitsToday,
        int UnifiVisitsToday);

    /// <summary>O endereço recém-gerado. Só aparece nesta resposta: depois, nem o painel consegue ver de novo.</summary>
    public record UnifiAddressDto(string Url);

    /// <summary>Um aparelho visto na rede de clientes nos últimos dias.</summary>
    /// <param name="Days">Em quantos dias apareceu.</param>
    /// <param name="AvgStayMinutes">Tempo médio por dia, só dos dias com aviso de desconexão; nulo = sem medida.</param>
    /// <param name="HasCustomer">Se o aparelho tem cadastro (o nome do cliente não aparece aqui).</param>
    public record UnifiDeviceDto(
        string Mac, string Hostname, int Days, DateTime LastSeenAt, double? AvgStayMinutes, bool HasCustomer);

    public record UnitIgnoredDeviceDto(string Mac, string Label, string Username, DateTime CreatedAt);

    public record UnifiDevicesDto(List<UnifiDeviceDto> Devices, List<UnitIgnoredDeviceDto> Ignored);

    public record IgnoreDeviceRequest(string Mac, string? Label);
}
