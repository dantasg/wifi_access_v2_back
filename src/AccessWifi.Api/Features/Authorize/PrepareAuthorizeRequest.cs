namespace AccessWifi.Api.Features.Authorize
{
    /// <summary>
    /// Corpo do POST /authorize/prepare: identifica a unidade (slug ou endereço do portal) e o
    /// aparelho. Sem dados pessoais — o formulário ainda nem foi preenchido.
    /// </summary>
    public record PrepareAuthorizeRequest(string? Unit, string? Host, string? Mac);
}
