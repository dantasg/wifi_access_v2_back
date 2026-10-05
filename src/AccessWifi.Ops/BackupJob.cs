using System.Formats.Tar;
using System.Runtime.Versioning;

namespace AccessWifi.Ops
{
    /// <summary>
    /// Backup diário (serviço accesswifi-backup, 03:15): banco + accesswifi.env (tem a Encryption__Key) + VERSAO
    /// num .tar, criptografado com AES-256 e a senha do backup (gpg), enviado ao Telegram. Ficam no servidor os
    /// 14 últimos dumps (só o banco, só o root lê). Restaurar: PRODUCAO.md §6.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public class BackupJob
    {
        private const int LocalCopies = 14;
        private const long TelegramLimit = 49L * 1024 * 1024;

        /// <summary>Simulação usa uma senha conhecida, para dar para conferir que o arquivo abre.</summary>
        public const string SimulationPassword = "simulacao-sem-senha";

        private readonly OpsSettings _objSettings;
        private readonly INotifier _objNotifier;
        private readonly TimeProvider _objTime;
        private readonly bool _bSimulate;

        public BackupJob(OpsSettings objSettings, INotifier objNotifier, TimeProvider objTime, bool bSimulate)
        {
            _objSettings = objSettings;
            _objNotifier = objNotifier;
            _objTime = objTime;
            _bSimulate = bSimulate;
        }

        public async Task<int> RunAsync()
        {
            DateTimeOffset dtNow = Clock.Now(_objTime);
            string sStamp = dtNow.ToString("yyyyMMdd-HHmmss");
            Directory.CreateDirectory(OpsPaths.BackupFolder);
            File.SetUnixFileMode(OpsPaths.BackupFolder,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string sDump = Path.Combine(OpsPaths.BackupFolder, $"diario-{sStamp}.dump");
            string sTemp = Directory.CreateTempSubdirectory("accesswifi-backup-").FullName;
            string? sKeep = null;

            try
            {
                ProcessResult objDump = await ProcessRunner.RunToFileAsync(
                    "runuser", ["-u", "postgres", "--", "pg_dump", "-Fc", OpsPaths.Database], sDump,
                    TimeSpan.FromMinutes(30));
                if (objDump.ExitCode != 0)
                {
                    throw new InvalidOperationException($"pg_dump falhou: {Cut(objDump.StdErr, 500)}");
                }

                ProcessResult objList = await ProcessRunner.RunAsync("pg_restore", ["--list", sDump]);
                int iTables = objList.StdOut.Split('\n').Count(sLine => sLine.Contains("TABLE DATA"));
                if (objList.ExitCode != 0 || iTables == 0)
                {
                    throw new InvalidOperationException("o arquivo gerado pelo pg_dump não abre (pg_restore --list falhou)");
                }

                foreach (string sOld in Directory.GetFiles(OpsPaths.BackupFolder, "diario-*.dump")
                             .OrderBy(sName => sName, StringComparer.Ordinal).SkipLast(LocalCopies))
                {
                    File.Delete(sOld);
                }

                string sPassword = _bSimulate ? SimulationPassword : _objSettings.Get(OpsSettings.KeyBackupPassword);
                if (sPassword.Length == 0)
                {
                    Console.WriteLine("backup local feito, mas a senha do backup não está configurada: nada foi enviado");
                    throw new InvalidOperationException("backup só local: falta configurar (rode: accesswifi-ops configurar)");
                }

                string sTar = Path.Combine(sTemp, $"accesswifi-{sStamp}.tar");
                await using (FileStream objTarFile = File.Create(sTar))
                await using (TarWriter objTar = new TarWriter(objTarFile))
                {
                    await objTar.WriteEntryAsync(sDump, "accesswifi.dump");
                    if (File.Exists(OpsPaths.AppConfig))
                    {
                        await objTar.WriteEntryAsync(OpsPaths.AppConfig, "accesswifi.env");
                    }
                    if (File.Exists(OpsPaths.AppVersion))
                    {
                        await objTar.WriteEntryAsync(OpsPaths.AppVersion, "VERSAO");
                    }
                }

                string sGnupg = Path.Combine(sTemp, "gnupg");
                Directory.CreateDirectory(sGnupg, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                string sEncrypted = sTar + ".gpg";
                ProcessResult objGpg = await ProcessRunner.RunAsync("gpg",
                [
                    "--homedir", sGnupg, "--batch", "--yes", "--pinentry-mode", "loopback", "--passphrase-fd", "0",
                    "--symmetric", "--cipher-algo", "AES256", "--output", sEncrypted, sTar,
                ], sInput: sPassword + "\n", tsTimeout: TimeSpan.FromMinutes(10));
                File.Delete(sTar);
                if (objGpg.ExitCode != 0)
                {
                    throw new InvalidOperationException($"criptografia falhou: {Cut(objGpg.StdErr, 300)}");
                }

                long lEncryptedSize = new FileInfo(sEncrypted).Length;
                if (lEncryptedSize > TelegramLimit)
                {
                    throw new InvalidOperationException(
                        $"backup com {Clock.Size(lEncryptedSize)}: maior que o limite do Telegram (50 MB)");
                }

                string sCaption = $"💾 Backup AccessWifi — {Clock.Format(dtNow)}\n"
                    + $"Banco: {Clock.Size(new FileInfo(sDump).Length)}, {iTables} tabelas com dados. "
                    + "Criptografado: abre só com a senha do backup (PRODUCAO.md §6).";
                if (_bSimulate)
                {
                    sKeep = sEncrypted;
                    Console.WriteLine($"[simulação] enviaria ao Telegram: {Path.GetFileName(sEncrypted)} "
                        + $"({Clock.Size(lEncryptedSize)})\n{sCaption}");
                    Console.WriteLine($"[simulação] arquivo mantido para conferência (senha \"{SimulationPassword}\"): "
                        + $"{sEncrypted} — apague depois");
                }
                else
                {
                    await new TelegramClient(_objSettings.Get(OpsSettings.KeyTelegramToken))
                        .SendDocumentAsync(_objSettings.Get(OpsSettings.KeyTelegramChat), sCaption, sEncrypted);
                    using (await OpsState.LockAsync())
                    {
                        OpsState objState = OpsState.Load();
                        objState.BackupOkAt = dtNow;
                        objState.BackupFailedAt = null;
                        objState.Save();
                    }
                }

                Console.WriteLine($"backup ok: {Path.GetFileName(sDump)} ({Clock.Size(new FileInfo(sDump).Length)}, {iTables} tabelas)");
                return 0;
            }
            catch (Exception objException)
            {
                Console.WriteLine($"BACKUP FALHOU: {objException.Message}");
                if (!_bSimulate)
                {
                    using (await OpsState.LockAsync())
                    {
                        OpsState objState = OpsState.Load();
                        objState.BackupFailedAt = dtNow;
                        objState.Save();
                    }
                }
                await _objNotifier.NotifyAsync("🔴 Backup do banco falhou",
                    $"{objException.Message}\n\nO backup de hoje NÃO foi guardado fora do servidor.");
                return 1;
            }
            finally
            {
                if (sKeep is null)
                {
                    Directory.Delete(sTemp, recursive: true);
                }
            }
        }

        private static string Cut(string sText, int iMax)
        {
            string sClean = sText.Trim();
            return sClean.Length > iMax ? sClean[..iMax] : sClean;
        }
    }
}
