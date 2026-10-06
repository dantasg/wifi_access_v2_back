using System.Reflection;
using AccessWifi.Ops;

// Rotinas de proteção do AccessWifi na VPS. Cada comando roda num serviço systemd próprio — se um travar,
// os outros seguem. No servidor, o atalho é "accesswifi-ops <comando>". Ver PRODUCAO.md §6 e §7.
const string Usage = """
    Rotinas de proteção do AccessWifi

      accesswifi-ops backup             backup do banco + configuração, criptografado, enviado ao Telegram
      accesswifi-ops vigiar             confere API, worker, banco, portal, disco, certificado e backup
      accesswifi-ops seguir-unifi       serviço: avisa NA HORA quando a UniFi recusa uma liberação
      accesswifi-ops avisar TITULO TEXTO
      accesswifi-ops configurar         passo guiado: robô do Telegram, senha do backup, e-mail dos avisos
      accesswifi-ops testar             manda uma mensagem de teste pelos canais configurados
      accesswifi-ops smtp-no-banco      usa a conta de e-mail dos avisos também no relatório mensal e nas campanhas
      accesswifi-ops versao

      --simular (backup/vigiar)         faz tudo, mas não manda nada: só mostra o que mandaria
    """;

bool bSimulate = args.Contains("--simular");
string[] arrArgs = args.Where(sArg => sArg != "--simular").ToArray();
string sCommand = arrArgs.Length > 0 ? arrArgs[0] : "";
TimeProvider objTime = TimeProvider.System;

if (sCommand == "versao")
{
    Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version);
    return 0;
}
if (!OperatingSystem.IsLinux())
{
    Console.WriteLine("As rotinas rodam só no servidor (Linux). Para testar a lógica: dotnet test tests/AccessWifi.Ops.Tests");
    return 2;
}

OpsSettings objSettings = OpsSettings.Load();
Notifier objNotifier = new Notifier(objSettings, objTime, bSimulate);

// Sem "using": o ProcessExit dispara DEPOIS do fim do programa, e cancelar uma fonte já descartada derrubava
// o processo com erro (código 134) a cada rodada. Ela vive até o processo acabar.
CancellationTokenSource objStop = new CancellationTokenSource();
Console.CancelKeyPress += (_, objEvent) =>
{
    objEvent.Cancel = true;
    objStop.Cancel();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => objStop.Cancel();

switch (sCommand)
{
    case "backup":
        return await new BackupJob(objSettings, objNotifier, objTime, bSimulate).RunAsync();
    case "vigiar":
        return await new HealthCheckJob(objNotifier, objTime, bSimulate).RunAsync();
    case "seguir-unifi":
        return await new UnifiWatcher(objNotifier, objTime).RunAsync(objStop.Token);
    case "avisar" when arrArgs.Length >= 2:
        await objNotifier.NotifyAsync(arrArgs[1], string.Join(' ', arrArgs.Skip(2)));
        return 0;
    case "configurar":
        return await new SetupWizard(objTime).RunAsync();
    case "testar":
        return await SetupWizard.TestChannelsAsync(objSettings, objTime);
    case "smtp-no-banco":
        return await SetupWizard.SyncReportSmtpAsync(objSettings) ? 0 : 1;
    default:
        Console.WriteLine(Usage);
        return 2;
}
