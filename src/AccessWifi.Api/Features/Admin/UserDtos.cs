namespace AccessWifi.Api.Features.Admin
{
    /// <summary>Unidade que um usuário de unidade enxerga.</summary>
    public record UserUnitDto(Guid Id, string Slug, string Name);

    /// <summary>
    /// RestrictToUnits = true cria um usuário de unidade (exige empresa e ao menos uma unidade dela);
    /// false = admin da empresa inteira (ou super admin, sem empresa).
    /// </summary>
    public record CreateUserRequest(
        string Username, string Password, Guid? IDCompany,
        bool RestrictToUnits = false, IReadOnlyList<Guid>? UnitIds = null);

    /// <summary>Campos nulos = manter. RestrictToUnits = true troca as unidades pelas de UnitIds.</summary>
    public record UpdateUserRequest(bool? Active = null, bool? RestrictToUnits = null, IReadOnlyList<Guid>? UnitIds = null);

    public record UserDto(
        Guid Id, string Username, string Role, Guid? IDCompany, string? CompanyName, DateTime CreatedAt, bool Active,
        bool RestrictToUnits, IReadOnlyList<UserUnitDto> Units);
}
