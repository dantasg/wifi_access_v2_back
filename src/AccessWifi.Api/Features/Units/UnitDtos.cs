using Models.DataBase;

namespace AccessWifi.Api.Features.Units
{
    public record UnitUnifiDto(string Host, string Site, string Username, bool UnifiOs, bool VerifySsl)
    {
        public static UnitUnifiDto FromEntity(CompanyUnifi objUnifi)
        {
            return new UnitUnifiDto(
                objUnifi.Host, objUnifi.Site, objUnifi.Username, objUnifi.UnifiOs, objUnifi.VerifySsl);
        }
    }

    public record UnitDto(
        Guid Id,
        Guid IDCompany,
        string Name,
        string Slug,
        bool Active,
        DateTime CreatedAt,
        UnitUnifiDto Unifi)
    {
        public static UnitDto FromEntity(Unit objUnit)
        {
            return new UnitDto(
                objUnit.Id, objUnit.IDCompany, objUnit.Name, objUnit.Slug, objUnit.Active,
                objUnit.CreatedAt, UnitUnifiDto.FromEntity(objUnit.Unifi));
        }
    }

    // Password nula = manter a senha atual (não expomos a senha na leitura).
    public record UnitUnifiRequest(string Host, string Site, string Username, string? Password, bool UnifiOs, bool VerifySsl);

    public record CreateUnitRequest(Guid IDCompany, string Name, string Slug, UnitUnifiRequest? Unifi);

    public record UpdateUnitRequest(string Name, bool Active, UnitUnifiRequest? Unifi);
}
