namespace AccessWifi.Api.Features.Campaigns;

/// <summary>Uma unidade na confirmação da demonstração: o e-mail que recebe e quantos clientes vão no PDF.</summary>
public sealed record CampaignDemoUnitDto(Guid Id, string Name, string Email, int Customers);

public sealed record CampaignDemoInfoDto(string CampaignName, string Message, List<CampaignDemoUnitDto> Units);

public sealed record CampaignDemoResultDto(string UnitName, string Email, int Customers, string Subject);
