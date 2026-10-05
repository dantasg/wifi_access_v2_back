using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Models.Persistence;

namespace AccessWifi.Ops
{
    /// <summary>
    /// Conferência de 5 em 5 minutos (serviço accesswifi-vigia): API, worker, banco, portal e certificado de cada
    /// endereço das unidades, vigia da UniFi, disco e backup atrasado. A tabela do que avisa e de quanto em
    /// quanto tempo repete está no PRODUCAO.md §7.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public class HealthCheckJob
    {
        private const int DiskLimitPercent = 85;
        private const int CertificateDays = 15;
        private const int BackupLateHours = 26;
        private const int MaintenanceMinutes = 5;

        private static readonly HttpClient s_objHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        private readonly INotifier _objNotifier;
        private readonly TimeProvider _objTime;
        private readonly bool _bSimulate;

        public HealthCheckJob(INotifier objNotifier, TimeProvider objTime, bool bSimulate)
        {
            _objNotifier = objNotifier;
            _objTime = objTime;
            _bSimulate = bSimulate;
        }

        public async Task<int> RunAsync()
        {
            using IDisposable objLock = await OpsState.LockAsync();
            OpsState objState = OpsState.Load();
            DateTimeOffset dtNow = Clock.Now(_objTime);
            objState.InstalledAt ??= dtNow;
            CheckTracker objTracker = new CheckTracker(objState, _objNotifier, _objTime);

            bool bMaintenance = File.Exists(OpsPaths.Maintenance)
                && DateTime.UtcNow - File.GetLastWriteTimeUtc(OpsPaths.Maintenance) < TimeSpan.FromMinutes(MaintenanceMinutes);
            if (bMaintenance)
            {
                Console.WriteLine("publicação recente: API, worker e portal não são conferidos agora");
            }
            else
            {
                (bool bApiOk, string sApiDetail) = await CheckApiAsync();
                await objTracker.CheckAsync("api", "API (portal e painel)", bApiOk,
                    $"{sApiDetail}. Ninguém consegue se cadastrar no Wi-Fi.", 1);

                string sWorker = await ProcessRunner.SystemctlStatusAsync("accesswifi-worker");
                await objTracker.CheckAsync("worker", "Worker (campanhas, relatórios e retenção)", sWorker == "active",
                    $"accesswifi-worker: {sWorker}. O portal segue funcionando; campanhas e relatórios param.", 6);
            }

            (bool bDbOk, string sDbDetail, List<string> lstHosts) = await CheckDatabaseAsync();
            await objTracker.CheckAsync("banco", "Banco de dados", bDbOk, sDbDetail, 1);

            foreach (string sHost in lstHosts)
            {
                (bool bPortalOk, string sPortalDetail, DateTimeOffset? dtExpires) = await CheckPortalAsync(sHost);
                if (!bMaintenance)
                {
                    await objTracker.CheckAsync($"portal:{sHost}", $"Portal {sHost}", bPortalOk, sPortalDetail, 1);
                }
                if (dtExpires is not null)
                {
                    int iDays = (int)(dtExpires.Value - DateTimeOffset.UtcNow).TotalDays;
                    await objTracker.CheckAsync($"certificado:{sHost}", $"Certificado HTTPS de {sHost}",
                        iDays >= CertificateDays,
                        $"O certificado de {sHost} vence em {iDays} dia(s), em {Clock.Format(dtExpires.Value)}. "
                        + "Sem ele, os celulares mostram \"site não seguro\".", 24);
                }
            }

            // As recusas da UniFi são avisadas NA HORA pelo serviço accesswifi-unifi (UnifiWatcher); aqui só se
            // confere que ele está de pé — sem ele, uma recusa passaria em silêncio.
            string sWatcher = await ProcessRunner.SystemctlStatusAsync("accesswifi-unifi");
            await objTracker.CheckAsync("vigia-unifi", "Vigia das liberações da UniFi", sWatcher == "active",
                $"accesswifi-unifi: {sWatcher}. Recusas da UniFi NÃO estão sendo avisadas na hora.", 6);

            DriveInfo objDisk = new DriveInfo("/");
            int iUsed = (int)Math.Round((objDisk.TotalSize - objDisk.AvailableFreeSpace) * 100.0 / objDisk.TotalSize);
            await objTracker.CheckAsync("disco", "Disco do servidor", iUsed < DiskLimitPercent,
                $"Disco em {iUsed}% ({Clock.Size(objDisk.AvailableFreeSpace)} livres).", 24);

            DateTimeOffset dtReference = objState.BackupOkAt ?? objState.InstalledAt.Value;
            string sLast = objState.BackupOkAt is null ? "nenhum ainda" : Clock.Format(objState.BackupOkAt.Value);
            await objTracker.CheckAsync("backup", "Backup diário", dtNow - dtReference < TimeSpan.FromHours(BackupLateHours),
                $"Nenhum backup guardado fora do servidor há mais de {BackupLateHours} h. Último: {sLast}.", 24);

            if (!_bSimulate)
            {
                objState.Save();
            }
            Console.WriteLine("vigia: conferência concluída");
            return 0;
        }

        /// <summary>Qualquer resposta HTTP abaixo de 500 prova que a API está de pé (sem parâmetro, /settings dá 400).</summary>
        private static async Task<(bool, string)> CheckApiAsync()
        {
            string sReason = "";
            for (int iAttempt = 1; iAttempt <= 3; iAttempt++)
            {
                try
                {
                    using HttpResponseMessage objResponse = await s_objHttp.GetAsync(OpsPaths.ApiLocal);
                    int iStatus = (int)objResponse.StatusCode;
                    if (iStatus < 500)
                    {
                        return (true, $"HTTP {iStatus}");
                    }
                    sReason = $"HTTP {iStatus}";
                }
                catch (Exception objException)
                {
                    sReason = objException.Message;
                }
                if (iAttempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10)); // um reinício (publicação) leva poucos segundos
                }
            }
            return (false, $"a API não responde em 127.0.0.1:5000 ({sReason})");
        }

        /// <summary>O banco responde? E, de quebra, quais endereços de portal as unidades ativas usam.</summary>
        private static async Task<(bool, string, List<string>)> CheckDatabaseAsync()
        {
            try
            {
                await using AppDbContext objDb = AppDatabase.Open();
                List<string> lstHosts = await objDb.Units.AsNoTracking()
                    .Where(unit => unit.Active && unit.PortalHost != "")
                    .Select(unit => unit.PortalHost.ToLower())
                    .Distinct()
                    .ToListAsync();
                return (true, "o banco responde", lstHosts);
            }
            catch (Exception objException)
            {
                string sMessage = objException.GetBaseException().Message;
                return (false, $"o banco não responde: {(sMessage.Length > 200 ? sMessage[..200] : sMessage)}",
                    new List<string>());
            }
        }

        /// <summary>
        /// Abre o portal pelo próprio servidor (nginx + certificado), com o nome de verdade (SNI). Devolve se
        /// respondeu 200, o detalhe e quando o certificado vence.
        /// </summary>
        private static async Task<(bool, string, DateTimeOffset?)> CheckPortalAsync(string sHost)
        {
            try
            {
                using TcpClient objTcp = new TcpClient();
                using CancellationTokenSource objTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await objTcp.ConnectAsync("127.0.0.1", 443, objTimeout.Token);
                await using SslStream objSsl = new SslStream(objTcp.GetStream());
                await objSsl.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions { TargetHost = sHost }, objTimeout.Token);
                X509Certificate2? objCertificate = objSsl.RemoteCertificate is null
                    ? null
                    : new X509Certificate2(objSsl.RemoteCertificate);
                DateTimeOffset? dtExpires = objCertificate is null
                    ? null
                    : new DateTimeOffset(objCertificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);

                byte[] arrRequest = Encoding.ASCII.GetBytes($"GET / HTTP/1.1\r\nHost: {sHost}\r\nConnection: close\r\n\r\n");
                await objSsl.WriteAsync(arrRequest, objTimeout.Token);
                byte[] arrBuffer = new byte[64];
                int iRead = await objSsl.ReadAsync(arrBuffer, objTimeout.Token);
                string sFirstLine = Encoding.ASCII.GetString(arrBuffer, 0, iRead).Split("\r\n")[0];
                string[] arrParts = sFirstLine.Split(' ');
                bool bOk = arrParts.Length > 1 && arrParts[1] == "200";
                return (bOk, $"https://{sHost} respondeu {(sFirstLine.Length > 0 ? sFirstLine : "nada")}", dtExpires);
            }
            catch (AuthenticationException objException)
            {
                return (false, $"certificado de {sHost} inválido: {objException.Message}", null);
            }
            catch (Exception objException)
            {
                return (false, $"https://{sHost} não abre: {objException.Message}", null);
            }
        }
    }
}
