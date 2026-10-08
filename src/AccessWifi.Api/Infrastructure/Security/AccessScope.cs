using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Infrastructure.Security;

/// <summary>
/// O que o usuário logado pode ver: super admin (tudo), admin da empresa (a empresa inteira) ou
/// usuário de unidade (só as unidades ligadas a ele). Lido do banco a cada requisição, para que
/// trocar as unidades de alguém valha na hora, sem esperar o token de acesso expirar.
/// </summary>
public sealed class AccessScope
{
    private readonly HashSet<Guid>? _objUnitIds;

    private AccessScope(Guid? objCompanyId, HashSet<Guid>? objUnitIds)
    {
        IDCompany = objCompanyId;
        _objUnitIds = objUnitIds;
    }

    /// <summary>Empresa do usuário; null = super admin.</summary>
    public Guid? IDCompany { get; }

    public bool IsSuperAdmin => IDCompany is null;

    /// <summary>Usuário de unidade: cadastros, campanhas e PDFs só das unidades dele.</summary>
    public bool IsUnitRestricted => _objUnitIds is not null;

    /// <summary>Admin da empresa inteira (pode gerenciar os usuários da empresa).</summary>
    public bool IsCompanyAdmin => IDCompany is not null && _objUnitIds is null;

    /// <summary>As unidades permitidas (vazio quando não há restrição — ver <see cref="IsUnitRestricted"/>).</summary>
    public Guid[] UnitIds => _objUnitIds?.ToArray() ?? [];

    public bool Allows(Guid? objUnitId) =>
        _objUnitIds is null || (objUnitId is Guid objId && _objUnitIds.Contains(objId));

    /// <summary>Restringe uma consulta de unidades ao que o usuário pode ver.</summary>
    public IQueryable<Unit> Apply(IQueryable<Unit> objUnits)
    {
        if (_objUnitIds is null)
        {
            return objUnits;
        }
        Guid[] arrIds = UnitIds;
        return objUnits.Where(unit => arrIds.Contains(unit.Id));
    }

    public static async Task<AccessScope> LoadAsync(
        AppDbContext objDbContext, ClaimsPrincipal objPrincipal, CancellationToken objCancellationToken)
    {
        Guid? objCompanyId = objPrincipal.GetCompanyId();
        if (objCompanyId is null)
        {
            return new AccessScope(null, null);
        }

        string sUsername = objPrincipal.GetUsername() ?? "";
        AdminUser? objUser = await objDbContext.Users.AsNoTracking()
            .Include(user => user.Units)
            .FirstOrDefaultAsync(user => user.Username == sUsername, objCancellationToken);

        // Sem o usuário no banco (não deveria acontecer com um token válido): não vê nenhuma unidade.
        if (objUser is null)
        {
            return new AccessScope(objCompanyId, []);
        }
        if (!objUser.RestrictToUnits)
        {
            return new AccessScope(objCompanyId, null);
        }

        List<Guid> objIds = objUser.Units.Select(link => link.IDUnit).ToList();
        // Só unidades da própria empresa contam, mesmo que alguma tenha mudado de empresa depois.
        HashSet<Guid> objDaEmpresa = (await objDbContext.Units.AsNoTracking()
                .Where(unit => unit.IDCompany == objCompanyId && objIds.Contains(unit.Id))
                .Select(unit => unit.Id)
                .ToListAsync(objCancellationToken))
            .ToHashSet();
        return new AccessScope(objCompanyId, objDaEmpresa);
    }
}
