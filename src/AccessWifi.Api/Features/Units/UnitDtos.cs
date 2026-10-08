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
        UnitUnifiDto Unifi,
        // Vazio = usa a URL "Geral" da empresa. Editada no formulário da unidade.
        string RedirectUrl,
        // E-mail do gerente: relatório mensal e PDF das campanhas (D17/D24). Vazio = não recebe.
        string Email,
        DateTime? LastReportSentAt,
        // Pontos de acesso lidos na nuvem da UniFi: é por eles que o portal sabe de qual loja é a visita.
        int DeviceCount = 0,
        DateTime? DevicesSyncedAt = null,
        string DevicesSyncError = "")
    {
        public static UnitDto FromEntity(Unit objUnit, int iDeviceCount = 0)
        {
            return new UnitDto(
                objUnit.Id, objUnit.IDCompany, objUnit.Name, objUnit.Slug, objUnit.Active,
                objUnit.CreatedAt, objUnit.PortalHost, UnitUnifiDto.FromEntity(objUnit.Unifi),
                objUnit.RedirectUrl, objUnit.Email, objUnit.LastReportSentAt,
                iDeviceCount, objUnit.DevicesSyncedAt, objUnit.DevicesSyncError);
        }
    }

    public record UnitDeviceDto(string Mac, string Name, string Model, DateTime SyncedAt)
    {
        public static UnitDeviceDto FromEntity(UnitDevice objDevice) =>
            new UnitDeviceDto(objDevice.Mac, objDevice.Name, objDevice.Model, objDevice.SyncedAt);
    }

    /// <summary>Resultado do botão "Ler pontos de acesso" (todas as unidades no modo nuvem).</summary>
    public record UnitDeviceSyncResponse(int Units, int Devices, int Failures);

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

    // PortalHost, RedirectUrl e Email: nulo = manter o atual; "" limpa (sem URL própria, a unidade usa
    // a "Geral" da empresa; sem e-mail, a unidade não recebe relatório nem campanhas).
    public record CreateUnitRequest(
        Guid IDCompany, string Name, string Slug, UnitUnifiRequest? Unifi, string? PortalHost = null,
        string? RedirectUrl = null, string? Email = null);

    public record UpdateUnitRequest(
        string Name, bool Active, UnitUnifiRequest? Unifi, string? PortalHost = null,
        string? RedirectUrl = null, string? Email = null);

    /// <summary>Resultado do botão "Testar conexão" (D7). Sucesso falso não é erro HTTP.</summary>
    public record UnifiTestResponse(bool Success, string Message);
}
