using AccessWifi.Api.Features.Companies;
using Models.DataBase;

namespace AccessWifi.Api.Infrastructure.Unifi
{
    public interface IUnifiClient
    {
        Task AuthorizeGuestAsync(
            CompanyUnifi objConfig, string sMac, int iAccessMinutes,
            CancellationToken objCancellationToken = default);
    }
}
