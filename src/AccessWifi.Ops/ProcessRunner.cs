using System.Diagnostics;

namespace AccessWifi.Ops
{
    public record ProcessResult(int ExitCode, string StdOut, string StdErr);

    /// <summary>Roda comandos do sistema (pg_dump, gpg, systemctl…) sem shell: cada argumento vai separado.</summary>
    public static class ProcessRunner
    {
        public static async Task<ProcessResult> RunAsync(
            string sFile, IEnumerable<string> objArgs, string? sInput = null, TimeSpan? tsTimeout = null)
        {
            using Process objProcess = Start(sFile, objArgs, bRedirectInput: sInput is not null);
            Task<string> objStdOut = objProcess.StandardOutput.ReadToEndAsync();
            Task<string> objStdErr = objProcess.StandardError.ReadToEndAsync();
            if (sInput is not null)
            {
                await objProcess.StandardInput.WriteAsync(sInput);
                objProcess.StandardInput.Close();
            }
            await WaitAsync(objProcess, tsTimeout ?? TimeSpan.FromMinutes(2), sFile);
            return new ProcessResult(objProcess.ExitCode, await objStdOut, await objStdErr);
        }

        /// <summary>A saída padrão vai direto para um arquivo (só o dono lê) — ex.: o pg_dump do backup.</summary>
        public static async Task<ProcessResult> RunToFileAsync(
            string sFile, IEnumerable<string> objArgs, string sOutputPath, TimeSpan tsTimeout)
        {
            using Process objProcess = Start(sFile, objArgs, bRedirectInput: false);
            Task<string> objStdErr = objProcess.StandardError.ReadToEndAsync();
            FileStreamOptions objOptions = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                objOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            await using (FileStream objOutput = new FileStream(sOutputPath, objOptions))
            {
                await objProcess.StandardOutput.BaseStream.CopyToAsync(objOutput);
            }
            await WaitAsync(objProcess, tsTimeout, sFile);
            return new ProcessResult(objProcess.ExitCode, "", await objStdErr);
        }

        public static async Task<string> SystemctlStatusAsync(string sUnit)
        {
            ProcessResult objResult = await RunAsync("systemctl", ["is-active", sUnit], tsTimeout: TimeSpan.FromSeconds(15));
            string sStatus = objResult.StdOut.Trim();
            return sStatus.Length > 0 ? sStatus : "desconhecido";
        }

        public static Process Start(string sFile, IEnumerable<string> objArgs, bool bRedirectInput)
        {
            ProcessStartInfo objInfo = new ProcessStartInfo(sFile)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = bRedirectInput,
                UseShellExecute = false,
            };
            foreach (string sArg in objArgs)
            {
                objInfo.ArgumentList.Add(sArg);
            }
            return Process.Start(objInfo) ?? throw new InvalidOperationException($"não foi possível iniciar {sFile}");
        }

        private static async Task WaitAsync(Process objProcess, TimeSpan tsTimeout, string sFile)
        {
            using CancellationTokenSource objTimeout = new CancellationTokenSource(tsTimeout);
            try
            {
                await objProcess.WaitForExitAsync(objTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                objProcess.Kill(entireProcessTree: true);
                throw new TimeoutException($"{sFile} passou de {tsTimeout.TotalSeconds:0} s e foi encerrado");
            }
        }
    }
}
