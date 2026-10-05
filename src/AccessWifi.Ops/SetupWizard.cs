using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Persistence;
using Models.Security;

namespace AccessWifi.Ops
{
    /// <summary>
    /// Passo guiado (accesswifi-ops configurar): robô do Telegram, senha do backup e e-mail dos avisos. Roda à mão,
    /// num terminal (ssh -t), porque pergunta senhas sem mostrá-las. Enter mantém o valor já salvo.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public class SetupWizard
    {
        private readonly TimeProvider _objTime;

        public SetupWizard(TimeProvider objTime)
        {
            _objTime = objTime;
        }

        public async Task<int> RunAsync()
        {
            if (Environment.UserName != "root" || Console.IsInputRedirected)
            {
                Console.WriteLine("Rode como root, num terminal (ssh -t ...).");
                return 1;
            }

            OpsSettings objSettings = OpsSettings.Load();
            Console.WriteLine("\n=== Rotinas de proteção do AccessWifi ===");
            Console.WriteLine($"Os valores ficam em {OpsPaths.Config} (só o root lê). Enter mantém o que já está.\n");

            if (!await SetupTelegramAsync(objSettings))
            {
                return 1;
            }
            SetupBackupPassword(objSettings);
            SetupEmail(objSettings);

            objSettings.Save();
            Console.WriteLine("\n✓ configuração salva.");

            if (Ask("\nUsar a mesma conta de e-mail no relatório mensal (gravar no banco)? [S/n]").ToLowerInvariant() != "n")
            {
                await SyncReportSmtpAsync(objSettings);
            }

            Console.WriteLine("\nTestando os dois canais...");
            await TestChannelsAsync(objSettings, _objTime);

            if (Ask("\nFazer o primeiro backup agora? [S/n]").ToLowerInvariant() != "n")
            {
                Notifier objNotifier = new Notifier(objSettings, _objTime, bSimulate: false);
                return await new BackupJob(objSettings, objNotifier, _objTime, bSimulate: false).RunAsync();
            }
            return 0;
        }

        private static async Task<bool> SetupTelegramAsync(OpsSettings objSettings)
        {
            Console.WriteLine("1) Robô do Telegram");
            string sToken;
            string sBot;
            while (true)
            {
                sToken = AskSecret("   Token do @BotFather", objSettings.Get(OpsSettings.KeyTelegramToken));
                try
                {
                    JsonElement objBot = await new TelegramClient(sToken)
                        .CallAsync("getMe", new Dictionary<string, string>());
                    sBot = objBot.GetProperty("username").GetString() ?? "";
                    Console.WriteLine($"   ✓ robô @{sBot}");
                    break;
                }
                catch (Exception objException)
                {
                    Console.WriteLine($"   ✗ token não aceito ({objException.Message}). Tente de novo.");
                }
            }
            objSettings.Set(OpsSettings.KeyTelegramToken, sToken);

            if (objSettings.Get(OpsSettings.KeyTelegramChat).Length > 0
                && Ask("   Manter o chat já configurado? [S/n]").ToLowerInvariant() != "n")
            {
                return true;
            }

            Console.WriteLine($"   Agora, no Telegram, abra o @{sBot} e mande /start (esperando até 3 minutos)...");
            TelegramClient objTelegram = new TelegramClient(sToken);
            DateTime dtLimit = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < dtLimit)
            {
                JsonElement objUpdates = await objTelegram.CallAsync("getUpdates",
                    new Dictionary<string, string> { ["timeout"] = "20" });
                foreach (JsonElement objUpdate in objUpdates.EnumerateArray())
                {
                    if (objUpdate.TryGetProperty("message", out JsonElement objMessage)
                        && objMessage.TryGetProperty("chat", out JsonElement objChat)
                        && objChat.GetProperty("type").GetString() == "private")
                    {
                        string sName = objChat.TryGetProperty("first_name", out JsonElement objName) ? objName.GetString() ?? "" : "";
                        Console.WriteLine($"   ✓ chat de {sName}");
                        objSettings.Set(OpsSettings.KeyTelegramChat, objChat.GetProperty("id").GetRawText());
                        return true;
                    }
                }
            }
            Console.WriteLine("   ✗ não chegou nenhuma mensagem. Rode o configurar de novo.");
            return false;
        }

        private static void SetupBackupPassword(OpsSettings objSettings)
        {
            Console.WriteLine("\n2) Senha do backup (o arquivo no Telegram só abre com ela)");
            if (objSettings.Get(OpsSettings.KeyBackupPassword).Length > 0
                && Ask("   Manter a senha atual? [S/n]").ToLowerInvariant() != "n")
            {
                return;
            }
            while (true)
            {
                string sPassword = AskSecret("   Nova senha (mínimo 12 caracteres)", "");
                if (sPassword.Length < 12)
                {
                    Console.WriteLine("   ✗ curta demais.");
                }
                else if (AskSecret("   Repita", "") != sPassword)
                {
                    Console.WriteLine("   ✗ as duas não batem.");
                }
                else
                {
                    objSettings.Set(OpsSettings.KeyBackupPassword, sPassword);
                    Console.WriteLine("   ⚠ Guarde essa senha num gerenciador de senhas. Sem ela, NENHUM backup abre.");
                    Console.WriteLine("   ⚠ Os backups antigos continuam abrindo só com a senha anterior.");
                    return;
                }
            }
        }

        private static void SetupEmail(OpsSettings objSettings)
        {
            Console.WriteLine("\n3) E-mail dos avisos");
            objSettings.Set(OpsSettings.KeyAlertEmails,
                Ask("   Quem recebe (separe por vírgula)", objSettings.Get(OpsSettings.KeyAlertEmails)));
            string sHost = Ask("   Servidor de envio", Default(objSettings.Get(OpsSettings.KeySmtpHost), "smtp.gmail.com"));
            objSettings.Set(OpsSettings.KeySmtpHost, sHost);
            objSettings.Set(OpsSettings.KeySmtpPort,
                Ask("   Porta (587; a 465 não é suportada)", Default(objSettings.Get(OpsSettings.KeySmtpPort), "587")));
            objSettings.Set(OpsSettings.KeySmtpUser,
                Ask("   Usuário (o e-mail que envia)", objSettings.Get(OpsSettings.KeySmtpUser)));
            string sPassword = AskSecret("   Senha (no Gmail, a \"senha de app\")", objSettings.Get(OpsSettings.KeySmtpPassword));
            // O Google mostra a senha de app em blocos com espaço ("abcd efgh …"); ela vale sem os espaços.
            objSettings.Set(OpsSettings.KeySmtpPassword, sHost.Contains("gmail") ? sPassword.Replace(" ", "") : sPassword);
            objSettings.Set(OpsSettings.KeySmtpFrom,
                Ask("   Remetente", Default(objSettings.Get(OpsSettings.KeySmtpFrom), objSettings.Get(OpsSettings.KeySmtpUser))));
            objSettings.Set(OpsSettings.KeySmtpFromName,
                Ask("   Nome do remetente", Default(objSettings.Get(OpsSettings.KeySmtpFromName), "AccessWifi Avisos")));
        }

        /// <summary>
        /// Grava a mesma conta nas chaves SMTP_* da tabela Configuration, que o worker usa no relatório mensal —
        /// assim existe uma conta só para configurar. A senha vai cifrada, com a mesma chave da API.
        /// </summary>
        public static async Task SyncReportSmtpAsync(OpsSettings objSettings)
        {
            try
            {
                IEncryptor objEncryptor = AppDatabase.Encryptor();
                Dictionary<string, string> dicValues = new Dictionary<string, string>
                {
                    ["SMTP_HOST"] = objSettings.Get(OpsSettings.KeySmtpHost),
                    ["SMTP_PORT"] = objSettings.SmtpPort.ToString(),
                    ["SMTP_USERNAME"] = objSettings.Get(OpsSettings.KeySmtpUser),
                    ["SMTP_PASSWORD"] = objEncryptor.Encrypt(objSettings.Get(OpsSettings.KeySmtpPassword)) ?? "",
                    ["SMTP_FROM_EMAIL"] = objSettings.SmtpFrom,
                    ["SMTP_FROM_NAME"] = "AccessWifi",
                    ["SMTP_USE_STARTTLS"] = "true",
                };

                await using AppDbContext objDb = AppDatabase.Open();
                foreach (KeyValuePair<string, string> objPair in dicValues)
                {
                    Configuration? objRow = await objDb.Configurations.FirstOrDefaultAsync(row => row.IDConfiguration == objPair.Key);
                    if (objRow is null)
                    {
                        objDb.Configurations.Add(new Configuration { IDConfiguration = objPair.Key, Value = objPair.Value });
                    }
                    else
                    {
                        objRow.Value = objPair.Value;
                    }
                }
                await objDb.SaveChangesAsync();
                Console.WriteLine("   ✓ relatório mensal usando a mesma conta (falta só o e-mail de relatório da empresa, no painel)");
            }
            catch (Exception objException)
            {
                Console.WriteLine($"   ✗ não deu para gravar no banco ({objException.GetBaseException().Message}); os avisos seguem funcionando");
            }
        }

        /// <summary>Manda uma mensagem de teste por cada canal e diz qual funcionou.</summary>
        public static async Task<int> TestChannelsAsync(OpsSettings objSettings, TimeProvider objTime)
        {
            const string Title = "🧪 Teste dos avisos do AccessWifi";
            string sText = "Se você recebeu isto, os avisos do servidor estão funcionando.\n\n"
                + $"— AccessWifi · {Clock.Format(Clock.Now(objTime))}";
            Notifier objNotifier = new Notifier(objSettings, objTime, bSimulate: false);
            int iResult = 0;
            foreach ((string sChannel, Func<Task> objSend) in new (string, Func<Task>)[]
            {
                ("Telegram", () => objNotifier.SendTelegramAsync($"{Title}\n\n{sText}")),
                ("e-mail", () => AlertEmail.SendAsync(objSettings, Title, sText)),
            })
            {
                try
                {
                    await objSend();
                    Console.WriteLine($"   ✓ {sChannel}: enviado");
                }
                catch (Exception objException)
                {
                    Console.WriteLine($"   ✗ {sChannel}: {objException.Message}");
                    iResult = 1;
                }
            }
            return iResult;
        }

        private static string Default(string sValue, string sFallback)
        {
            return sValue.Length > 0 ? sValue : sFallback;
        }

        private static string Ask(string sQuestion, string sCurrent = "")
        {
            Console.Write(sCurrent.Length > 0 ? $"{sQuestion} [{sCurrent}]: " : $"{sQuestion}: ");
            string sAnswer = (Console.ReadLine() ?? "").Trim();
            return sAnswer.Length > 0 ? sAnswer : sCurrent;
        }

        /// <summary>Pergunta sem mostrar o que é digitado. Enter vazio mantém o valor atual.</summary>
        private static string AskSecret(string sQuestion, string sCurrent)
        {
            Console.Write(sCurrent.Length > 0 ? $"{sQuestion} [mantém o atual]: " : $"{sQuestion}: ");
            StringBuilder objTyped = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo objKey = Console.ReadKey(intercept: true);
                if (objKey.Key == ConsoleKey.Enter)
                {
                    break;
                }
                if (objKey.Key == ConsoleKey.Backspace)
                {
                    if (objTyped.Length > 0)
                    {
                        objTyped.Length--;
                    }
                }
                else if (!char.IsControl(objKey.KeyChar))
                {
                    objTyped.Append(objKey.KeyChar);
                }
            }
            Console.WriteLine();
            string sTyped = objTyped.ToString().Trim();
            return sTyped.Length > 0 ? sTyped : sCurrent;
        }
    }
}
