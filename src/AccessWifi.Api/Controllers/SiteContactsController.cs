using AccessWifi.Api.Features;
using AccessWifi.Api.Features.SiteContacts;
using AccessWifi.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Persistence;

namespace AccessWifi.Api.Controllers;

/// <summary>
/// Tela "Contatos do site" (PROPOSTA_SITE.md §4): os pedidos que chegaram pelo formulário do site institucional, de
/// onde vieram e o andamento de cada um. Só o super admin: são contatos do AccessWifi, não de uma empresa cliente.
/// </summary>
[ApiController]
[Route("admin/site-contacts")]
[Authorize(Roles = ClaimsExtensions.RoleSuperAdmin)]
public class SiteContactsController : ControllerBase
{
    private const int MaxPageSize = 100;
    private const int MaxNotes = 2000;

    private readonly AppDbContext _objDbContext;

    public SiteContactsController(AppDbContext objDbContext)
    {
        _objDbContext = objDbContext;
    }

    /// <summary>Lista do mais novo para o mais antigo, com filtro por situação e busca por nome, empresa, cidade ou telefone.</summary>
    [HttpGet]
    public async Task<ActionResult<SiteContactPageDto>> List(
        [FromQuery(Name = "status")] string? sStatus,
        [FromQuery(Name = "search")] string? sSearch,
        [FromQuery(Name = "page")] int iPage,
        [FromQuery(Name = "pageSize")] int iPageSize,
        CancellationToken objCancellationToken)
    {
        iPage = Math.Max(1, iPage);
        iPageSize = iPageSize <= 0 ? 20 : Math.Min(iPageSize, MaxPageSize);

        IQueryable<SiteContact> objQuery = _objDbContext.SiteContacts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sStatus))
        {
            if (!Enum.TryParse(sStatus, ignoreCase: true, out SiteContactStatus eStatus) || !Enum.IsDefined(eStatus))
            {
                return BadRequest(new ErrorResponse("Situação inválida."));
            }
            objQuery = objQuery.Where(contact => contact.Status == eStatus);
        }

        string sTerm = (sSearch ?? "").Trim().ToLowerInvariant();
        if (sTerm.Length > 0)
        {
            string sDigits = new string(sTerm.Where(char.IsAsciiDigit).ToArray());
            objQuery = objQuery.Where(contact =>
                contact.Name.ToLower().Contains(sTerm)
                || contact.Company.ToLower().Contains(sTerm)
                || contact.City.ToLower().Contains(sTerm)
                || contact.Email.Contains(sTerm)
                || (sDigits.Length >= 4 && contact.Phone.Contains(sDigits)));
        }

        int iTotal = await objQuery.CountAsync(objCancellationToken);
        List<SiteContact> objContacts = await objQuery
            .OrderByDescending(contact => contact.CreatedAt)
            .Skip((iPage - 1) * iPageSize)
            .Take(iPageSize)
            .ToListAsync(objCancellationToken);

        var objCounts = await _objDbContext.SiteContacts.AsNoTracking()
            .GroupBy(contact => contact.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(objCancellationToken);
        int CountOf(SiteContactStatus eStatus) => objCounts.FirstOrDefault(item => item.Status == eStatus)?.Count ?? 0;

        return Ok(new SiteContactPageDto(
            objContacts.Select(SiteContactDto.FromEntity).ToList(),
            iTotal,
            new SiteContactCountsDto(
                CountOf(SiteContactStatus.New),
                CountOf(SiteContactStatus.Talking),
                CountOf(SiteContactStatus.Client),
                CountOf(SiteContactStatus.Discarded))));
    }

    /// <summary>Muda a situação e/ou as anotações. Quem mudou a situação e quando ficam gravados.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SiteContactDto>> Update(
        Guid id, SiteContactUpdateRequest objRequest, CancellationToken objCancellationToken)
    {
        SiteContact? objContact = await _objDbContext.SiteContacts.FirstOrDefaultAsync(contact => contact.Id == id, objCancellationToken);
        if (objContact is null)
        {
            return NotFound(new ErrorResponse("Contato não encontrado."));
        }

        if (objRequest.Status is not null)
        {
            if (!Enum.TryParse(objRequest.Status, ignoreCase: true, out SiteContactStatus eStatus)
                || !Enum.IsDefined(eStatus))
            {
                return BadRequest(new ErrorResponse("Situação inválida."));
            }
            if (eStatus != objContact.Status)
            {
                objContact.Status = eStatus;
                objContact.StatusChangedAt = DateTime.UtcNow;
                objContact.StatusChangedBy = User.GetUsername() ?? "";
            }
        }

        if (objRequest.Notes is not null)
        {
            string sNotes = objRequest.Notes.Trim();
            if (sNotes.Length > MaxNotes)
            {
                return BadRequest(new ErrorResponse($"As anotações devem ter no máximo {MaxNotes} caracteres."));
            }
            objContact.Notes = sNotes;
        }

        await _objDbContext.SaveChangesAsync(objCancellationToken);
        return Ok(SiteContactDto.FromEntity(objContact));
    }

    /// <summary>Apaga o contato (pedido de exclusão da pessoa, LGPD, ou contato de teste).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken objCancellationToken)
    {
        SiteContact? objContact = await _objDbContext.SiteContacts.FirstOrDefaultAsync(contact => contact.Id == id, objCancellationToken);
        if (objContact is null)
        {
            return NotFound(new ErrorResponse("Contato não encontrado."));
        }
        _objDbContext.SiteContacts.Remove(objContact);
        await _objDbContext.SaveChangesAsync(objCancellationToken);
        return NoContent();
    }
}
