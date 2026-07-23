using AccessWifi.Api.Features;
using AccessWifi.Api.Features.Admin;
using Models.Persistence;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;

namespace AccessWifi.Api.Controllers;

[ApiController]
[Route("admin/users")]
[Authorize(Roles = ClaimsExtensions.RoleSuperAdmin)]
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
        IQueryable<AdminUser> objQuery = _objDbContext.Users
            .AsNoTracking()
            .Include(user => user.Company);

        if (!string.IsNullOrWhiteSpace(sCompanySlug))
        {
            objQuery = objQuery.Where(user => user.Company != null && user.Company.Slug == sCompanySlug);
        }

        List<UserDto> objUsers = await objQuery
            .OrderBy(user => user.Username)
            .Select(user => new UserDto(
                user.Id,
                user.Username,
                user.IDCompany == null ? ClaimsExtensions.RoleSuperAdmin : ClaimsExtensions.RoleAdmin,
                user.IDCompany,
                user.Company == null ? null : user.Company.Name,
                user.CreatedAt))
            .ToListAsync(objCancellationToken);

        return Ok(objUsers);
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest objRequest, CancellationToken objCancellationToken)
    {
        string sUsername = objRequest.Username?.Trim().ToLowerInvariant() ?? "";
        if (sUsername.Length < 3 || sUsername.Length > 60 || sUsername.Contains(' '))
        {
            return BadRequest(new ErrorResponse(
                "Usuário inválido: entre 3 e 60 caracteres, sem espaços."));
        }

        if (string.IsNullOrEmpty(objRequest.Password) || objRequest.Password.Length < 8)
        {
            return BadRequest(new ErrorResponse("Senha deve ter no mínimo 8 caracteres."));
        }

        bool usernameEmUso = await _objDbContext.Users
            .AnyAsync(user => user.Username == sUsername, objCancellationToken);
        if (usernameEmUso)
        {
            return BadRequest(new ErrorResponse("Já existe um usuário com esse nome."));
        }

        Company? objCompany = null;
        if (objRequest.IDCompany is not null)
        {
            objCompany = await _objDbContext.Companies.FirstOrDefaultAsync(
                company => company.Id == objRequest.IDCompany, objCancellationToken);
            if (objCompany is null)
            {
                return BadRequest(new ErrorResponse("Empresa não encontrada."));
            }
        }

        AdminUser objUser = new AdminUser
        {
            Username = sUsername,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(objRequest.Password),
            IDCompany = objRequest.IDCompany,
        };
        _objDbContext.Users.Add(objUser);
        await _objDbContext.SaveChangesAsync(objCancellationToken);

        return Ok(new UserDto(
            objUser.Id,
            objUser.Username,
            objUser.IDCompany is null ? ClaimsExtensions.RoleSuperAdmin : ClaimsExtensions.RoleAdmin,
            objUser.IDCompany,
            objCompany?.Name,
            objUser.CreatedAt));
    }
}
