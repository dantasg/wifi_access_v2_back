using Models.DataBase;

namespace AccessWifi.Api.Features.Units
{
    // Nem a senha da controladora nem a chave da nuvem são devolvidas na leitura; HasApiKey só
    // conta se existe uma chave guardada, para a tela poder mostrar "configurada".
    public record UnitUnifiDto(
        string Mode,
        string Host,
        string Site,
        string Username,
        bool UnifiOs,
        bool VerifySsl,
        string ConsoleId,
        string SiteId,
        bool HasApiKey)
    {
        public static UnitUnifiDto FromEntity(CompanyUnifi objUnifi)
        {
            return new UnitUnifiDto(
                objUnifi.Mode, objUnifi.Host, objUnifi.Site, objUnifi.Username, objUnifi.UnifiOs,
                objUnifi.VerifySsl, objUnifi.ConsoleId, objUnifi.SiteId,
                !string.IsNullOrWhiteSpace(objUnifi.ApiKey));
        }
    }

    public record UnitDto(
        Guid Id,
        Guid IDCompany,
        string Name,
        string Slug,
        bool Active,
        DateTime CreatedAt,
        string PortalHost,
        UnitUnifiDto Unifi)
    {
        public static UnitDto FromEntity(Unit objUnit)
        {
            return new UnitDto(
                objUnit.Id, objUnit.IDCompany, objUnit.Name, objUnit.Slug, objUnit.Active,
                objUnit.CreatedAt, objUnit.PortalHost, UnitUnifiDto.FromEntity(objUnit.Unifi));
        }
    }

    // Password/ApiKey nulos = manter os atuais (não expomos nenhum dos dois na leitura).
    // Mode nulo = "Local", que é o comportamento histórico.
    public record UnitUnifiRequest(
        string Host,
        string Site,
        string Username,
        string? Password,
        bool UnifiOs,
        bool VerifySsl,
        string? Mode = null,
        string? ConsoleId = null,
        string? ApiKey = null,
        string? SiteId = null);

    // PortalHost nulo = manter o atual; "" limpa.
    public record CreateUnitRequest(
        Guid IDCompany, string Name, string Slug, UnitUnifiRequest? Unifi, string? PortalHost = null);

    public record UpdateUnitRequest(
        string Name, bool Active, UnitUnifiRequest? Unifi, string? PortalHost = null);

    /// <summary>Resultado do botão "Testar conexão" (D7). Sucesso falso não é erro HTTP.</summary>
    public record UnifiTestResponse(bool Success, string Message);
}
