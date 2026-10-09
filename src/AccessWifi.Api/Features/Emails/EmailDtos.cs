using Models.DataBase;

namespace AccessWifi.Api.Features.Emails;

/// <summary>Uma linha do Correio eletrônico (sem o texto, que vem no detalhe).</summary>
public record SentEmailListItemDto(
    Guid Id,
    string Kind,
    Guid? IDUnit,
    string UnitName,
    string ToEmail,
    string Subject,
    string AttachmentName,
    DateTime SentAt)
{
    public static SentEmailListItemDto FromEntity(SentEmail objEmail) =>
        new SentEmailListItemDto(
            objEmail.Id, objEmail.Kind, objEmail.IDUnit, objEmail.UnitName, objEmail.ToEmail,
            objEmail.Subject, objEmail.AttachmentName, objEmail.SentAt);
}

/// <summary>Página da lista: os itens e o total do filtro (para a paginação).</summary>
public record SentEmailPageDto(IReadOnlyList<SentEmailListItemDto> Items, int Total);

/// <summary>O e-mail inteiro. <c>Body</c> nulo = enviado antes do registro existir (o texto não foi guardado).</summary>
public record SentEmailDto(
    Guid Id,
    string Kind,
    Guid? IDUnit,
    string UnitName,
    string ToEmail,
    string Subject,
    string? Body,
    string AttachmentName,
    DateTime SentAt)
{
    public static SentEmailDto FromEntity(SentEmail objEmail) =>
        new SentEmailDto(
            objEmail.Id, objEmail.Kind, objEmail.IDUnit, objEmail.UnitName, objEmail.ToEmail,
            objEmail.Subject, objEmail.Body, objEmail.AttachmentName, objEmail.SentAt);
}
