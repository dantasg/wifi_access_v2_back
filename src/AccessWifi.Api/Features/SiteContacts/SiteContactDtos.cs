using Models.DataBase;

namespace AccessWifi.Api.Features.SiteContacts;

/// <summary>
/// O que o formulário do site manda (PROPOSTA_SITE.md §4). Tudo opcional no JSON: a validação fica em
/// <see cref="SiteContactRules"/>, com a mensagem certa para cada campo. <c>Website</c> é o campo-isca: pessoas não
/// veem, robôs costumam preencher.
/// </summary>
public record SiteContactRequest(
    string? Name,
    string? Company,
    string? Phone,
    string? Email,
    string? City,
    string? State,
    string? Stores,
    string? UsesUnifi,
    string? Message,
    bool Consent,
    string? Website,
    string? Page,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string? UtmTerm,
    string? UtmContent,
    string? Gclid,
    string? Fbclid,
    string? LandingPage,
    string? Referrer);

/// <summary>Um contato na tela "Contatos do site" (só super admin).</summary>
public record SiteContactDto(
    Guid Id,
    string Name,
    string Company,
    string Phone,
    string Email,
    string City,
    string State,
    string Stores,
    string UsesUnifi,
    string Message,
    string UtmSource,
    string UtmMedium,
    string UtmCampaign,
    string UtmTerm,
    string UtmContent,
    bool FromGoogleAd,
    bool FromMetaAd,
    string LandingPage,
    string Referrer,
    string Page,
    string Status,
    string Notes,
    DateTime? StatusChangedAt,
    string StatusChangedBy,
    DateTime? NotifiedAt,
    string NotifyError,
    DateTime CreatedAt)
{
    public static SiteContactDto FromEntity(SiteContact objContact) =>
        new SiteContactDto(
            objContact.Id, objContact.Name, objContact.Company, objContact.Phone, objContact.Email,
            objContact.City, objContact.State, objContact.Stores, objContact.UsesUnifi, objContact.Message,
            objContact.UtmSource, objContact.UtmMedium, objContact.UtmCampaign, objContact.UtmTerm, objContact.UtmContent,
            objContact.Gclid.Length > 0, objContact.Fbclid.Length > 0,
            objContact.LandingPage, objContact.Referrer, objContact.Page,
            objContact.Status.ToString(), objContact.Notes, objContact.StatusChangedAt, objContact.StatusChangedBy,
            objContact.NotifiedAt, objContact.NotifyError, objContact.CreatedAt);
}

/// <summary>Quantos contatos há em cada situação (as abas da tela), sem filtro de busca.</summary>
public record SiteContactCountsDto(int New, int Talking, int Client, int Discarded);

/// <summary>Página da lista: os itens, o total do filtro (paginação) e as contagens das abas.</summary>
public record SiteContactPageDto(IReadOnlyList<SiteContactDto> Items, int Total, SiteContactCountsDto Counts);

/// <summary>Muda a situação e as anotações de um contato.</summary>
public record SiteContactUpdateRequest(string? Status, string? Notes);
