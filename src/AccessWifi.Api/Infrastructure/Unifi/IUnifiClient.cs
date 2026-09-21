using Models.DataBase;

namespace AccessWifi.Api.Infrastructure.Unifi
{
    public interface IUnifiClient
    {
        /// <summary>
        /// Libera o dispositivo do visitante na controladora da unidade.
        /// </summary>
        /// <remarks>
        /// No modo nuvem, quando <see cref="CompanyUnifi.SiteId"/> está vazio ele é descoberto e
        /// gravado em <paramref name="objConfig"/> — quem chamou deve persistir a entidade para
        /// não repetir a descoberta a cada visitante.
        /// </remarks>
        Task AuthorizeGuestAsync(
            CompanyUnifi objConfig, string sMac, int iAccessMinutes,
            CancellationToken objCancellationToken = default);

        /// <summary>
        /// Valida a configuração da unidade sem autorizar ninguém e devolve uma descrição do que
        /// foi encontrado. Lança <see cref="UnifiException"/> quando a configuração não serve.
        /// Igual ao método acima, pode preencher o <see cref="CompanyUnifi.SiteId"/>.
        /// </summary>
        Task<string> TestConnectionAsync(
            CompanyUnifi objConfig, CancellationToken objCancellationToken = default);

        /// <summary>
        /// Adianta, em segundo plano, o que der para adiantar da autorização deste aparelho — chamado
        /// quando o portal abre, enquanto o visitante ainda preenche o formulário. Retorna na hora e
        /// nunca lança: se não adiantar nada, o AuthorizeGuestAsync faz o trabalho completo.
        /// </summary>
        Task PrepareAsync(
            CompanyUnifi objConfig, string sMac, CancellationToken objCancellationToken = default);
    }
}
