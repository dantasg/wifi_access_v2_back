using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Admin;
using Models.Persistence;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// Usuários do painel. O super admin gerencia todos; o admin da empresa, só os da própria empresa
/// (admins da empresa inteira e usuários de unidade). O usuário de unidade não entra aqui.
/// </summary>
[ApiController]
[Route("admin/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _objDbContext;

    public UsersController(AppDbContext objDbContext)
    {
        _objDbContext = objDbContext;
    }

    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetAll(
        [FromQuery(Name = "company")] string? sCompanySlug, CancellationToken objCancellationToken)
    {
        AccessScope objScope = await AccessScope.LoadAsync(_objDbContext, User, objCancellationToken);
        if (objScope.IsUnitRestricted)
        {
            return Forbid();
        }

        IQueryable<AdminUser> objQuery = _objDbContext.Users
            .AsNoTracking()
            .Include(user => user.Company)
            .Include(user => user.Units);

        if (!objScope.IsSuperAdmin)
        {
            objQuery = objQuery.Where(user => user.IDCompany == objScope.IDCompany);
        }
        else if (!string.IsNullOrWhiteSpace(sCompanySlug))
        {
            objQuery = objQuery.Where(user => user.Company != null && user.Company.Slug == sCompanySlug);
        }

        List<AdminUser> objUsers = await objQuery
            .OrderBy(user => user.Username)
            .ToListAsync(objCancellationToken);
        Dictionary<Guid, UserUnitDto> objUnits = await UnitRefsAsync(
            objUsers.SelectMany(user => user.Units.Select(link => link.IDUnit)), objCancellationToken);

        return Ok(objUsers.Select(user => ToDto(user, objUnits)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest objRequest, CancellationToken objCancellationToken)
    {
        AccessScope objScope = await AccessScope.LoadAsync(_objDbContext, User, objCancellationToken);
        if (objScope.IsUnitRestricted)
        {
            return Forbid();
        }

        string sUsername = UserRules.NormalizeUsername(objRequest.Username);
        string? sFieldsError = UserRules.ValidateUsername(sUsername) ?? UserRules.ValidatePassword(objRequest.Password);
        if (sFieldsError is not null)
        {
            return BadRequest(new ErrorResponse(sFieldsError));
        }

        bool usernameInUse = await _objDbContext.Users
            .AnyAsync(user => user.Username == sUsername, objCancellationToken);
        if (usernameInUse)
        {
            return BadRequest(new ErrorResponse("Já existe um usuário com esse nome."));
        }

        // O admin da empresa só cria usuários da própria empresa (nunca super admin).
        Guid? objCompanyId = objScope.IsSuperAdmin ? objRequest.IDCompany : objScope.IDCompany;
        Company? objCompany = null;
        if (objCompanyId is not null)
        {
            objCompany = await _objDbContext.Companies.FirstOrDefaultAsync(
                company => company.Id == objCompanyId, objCancellationToken);
            if (objCompany is null)
            {
                return BadRequest(new ErrorResponse("Empresa não encontrada."));
            }
        }

        List<Guid> objUnitIds = [];
        if (objRequest.RestrictToUnits)
        {
            (List<Guid>? objValid, string? sError) = await ValidateUnitsAsync(
                objCompanyId, objRequest.UnitIds, objCancellationToken);
            if (objValid is null)
            {
                return BadRequest(new ErrorResponse(sError!));
            }
            objUnitIds = objValid;
        }

        AdminUser objUser = new AdminUser
        {
            Username = sUsername,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(objRequest.Password),
            IDCompany = objCompanyId,
            RestrictToUnits = objRequest.RestrictToUnits,
            Units = objUnitIds.Select(objUnitId => new AdminUserUnit { IDUnit = objUnitId }).ToList(),
        };
        _objDbContext.Users.Add(objUser);
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        objUser.Company = objCompany;
        Dictionary<Guid, UserUnitDto> objUnits = await UnitRefsAsync(objUnitIds, objCancellationToken);
        return Ok(ToDto(objUser, objUnits));
    }

    /// <summary>
    /// Ativa/desativa um usuário e/ou troca as unidades dele. Desativar encerra as sessões dele
    /// (revoga os refresh tokens); o access token já emitido ainda vale até expirar (~1h). Trocar as
    /// unidades vale na hora: o acesso é conferido no banco a cada requisição.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserDto>> Update(
        Guid id, UpdateUserRequest objRequest, CancellationToken objCancellationToken)
    {
        AccessScope objScope = await AccessScope.LoadAsync(_objDbContext, User, objCancellationToken);
        if (objScope.IsUnitRestricted)
        {
            return Forbid();
        }

        AdminUser? objUser = await _objDbContext.Users
            .Include(user => user.Company)
            .Include(user => user.Units)
            .FirstOrDefaultAsync(user => user.Id == id, objCancellationToken);
        // O admin da empresa não enxerga usuários de outras empresas nem super admins.
        if (objUser is null || (!objScope.IsSuperAdmin && objUser.IDCompany != objScope.IDCompany))
        {
            return NotFound(new ErrorResponse("Usuário não encontrado."));
        }

        bool bOwn = objUser.Username == User.GetUsername();

        if (objRequest.RestrictToUnits is bool bRestrict)
        {
            if (objUser.IDCompany is null)
            {
                return BadRequest(new ErrorResponse("Super admin não fica preso a unidades."));
            }
            if (bOwn)
            {
                return BadRequest(new ErrorResponse("Você não pode mudar o próprio acesso."));
            }

            List<Guid> objUnitIds = [];
            if (bRestrict)
            {
                (List<Guid>? objValid, string? sError) = await ValidateUnitsAsync(
                    objUser.IDCompany, objRequest.UnitIds, objCancellationToken);
                if (objValid is null)
                {
                    return BadRequest(new ErrorResponse(sError!));
                }
                objUnitIds = objValid;
            }

            objUser.Units.Clear();
            foreach (Guid objUnitId in objUnitIds)
            {
                objUser.Units.Add(new AdminUserUnit { IDUser = objUser.Id, IDUnit = objUnitId });
            }
            objUser.RestrictToUnits = bRestrict;
        }

        if (objRequest.Active is bool bActive && objUser.Active && !bActive)
        {
            if (bOwn)
            {
                return BadRequest(new ErrorResponse("Você não pode desativar o próprio usuário."));
            }

            if (objUser.IDCompany is null)
            {
                bool bAnotherActiveSuperAdminExists = await _objDbContext.Users.AnyAsync(
                    user => user.IDCompany == null && user.Active && user.Id != objUser.Id,
                    objCancellationToken);
                if (!bAnotherActiveSuperAdminExists)
                {
                    return BadRequest(new ErrorResponse(
                        "Não é possível desativar o último super admin ativo."));
                }
            }

            DateTime dtNowUtc = DateTime.UtcNow;
            List<RefreshToken> objTokens = await _objDbContext.RefreshTokens
                .Where(token => token.IDUser == objUser.Id && token.RevokedAt == null)
                .ToListAsync(objCancellationToken);
            foreach (RefreshToken objToken in objTokens)
            {
                objToken.RevokedAt = dtNowUtc;
            }
        }

        if (objRequest.Active is bool bNewActive)
        {
            objUser.Active = bNewActive;
        }
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        Dictionary<Guid, UserUnitDto> objUnits = await UnitRefsAsync(
            objUser.Units.Select(link => link.IDUnit), objCancellationToken);
        return Ok(ToDto(objUser, objUnits));
    }

    /// <summary>
    /// Usuário de unidade: precisa de empresa e de ao menos uma unidade, todas da mesma empresa.
    /// Devolve as unidades sem repetição, ou null e o motivo.
    /// </summary>
    private async Task<(List<Guid>?, string?)> ValidateUnitsAsync(
        Guid? objCompanyId, IReadOnlyList<Guid>? objUnitIds, CancellationToken objCancellationToken)
    {
        if (objCompanyId is null)
        {
            return (null, "Usuário de unidade precisa de uma empresa.");
        }

        List<Guid> objIds = (objUnitIds ?? []).Distinct().ToList();
        if (objIds.Count == 0)
        {
            return (null, "Escolha ao menos uma unidade.");
        }

        int iInCompany = await _objDbContext.Units.CountAsync(
            unit => unit.IDCompany == objCompanyId && objIds.Contains(unit.Id), objCancellationToken);
        return iInCompany == objIds.Count
            ? (objIds, null)
            : (null, "Unidade não encontrada nesta empresa.");
    }

    private async Task<Dictionary<Guid, UserUnitDto>> UnitRefsAsync(
        IEnumerable<Guid> objIds, CancellationToken objCancellationToken)
    {
        List<Guid> objList = objIds.Distinct().ToList();
        if (objList.Count == 0)
        {
            return [];
        }
        return await _objDbContext.Units.AsNoTracking()
            .Where(unit => objList.Contains(unit.Id))
            .ToDictionaryAsync(unit => unit.Id, unit => new UserUnitDto(unit.Id, unit.Slug, unit.Name), objCancellationToken);
    }

    private static UserDto ToDto(AdminUser objUser, Dictionary<Guid, UserUnitDto> objUnits) =>
        new UserDto(
            objUser.Id,
            objUser.Username,
            objUser.IDCompany is null ? ClaimsExtensions.RoleSuperAdmin : ClaimsExtensions.RoleAdmin,
            objUser.IDCompany,
            objUser.Company?.Name,
            objUser.CreatedAt,
            objUser.Active,
            objUser.RestrictToUnits,
            objUser.Units
                .Select(link => objUnits.GetValueOrDefault(link.IDUnit))
                .OfType<UserUnitDto>()
                .OrderBy(unit => unit.Name)
                .ToList());
}
