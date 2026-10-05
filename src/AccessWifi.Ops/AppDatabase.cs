using Microsoft.EntityFrameworkCore;
using Models.Persistence;
using Models.Security;

namespace AccessWifi.Ops
{
    /// <summary>
    /// O banco da aplicação, com a mesma configuração da API (/etc/accesswifi/accesswifi.env). Lido do arquivo
    /// (e não de variáveis de ambiente) para funcionar igual no systemd e quando alguém roda à mão pelo SSH.
    /// </summary>
    public static class AppDatabase
    {
        private const string KeyConnection = "ConnectionStrings__Default";
        private const string KeyEncryption = "Encryption__Key";

        public static AppDbContext Open()
        {
            Dictionary<string, string> dicConfig = EnvFile.Read(OpsPaths.AppConfig);
            if (!dicConfig.TryGetValue(KeyConnection, out string? sConnection) || sConnection.Length == 0)
            {
                throw new InvalidOperationException($"{KeyConnection} não está em {OpsPaths.AppConfig}");
            }
            DbContextOptions<AppDbContext> objOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(sConnection)
                .Options;
            return new AppDbContext(objOptions);
        }

        /// <summary>Mesma cifra da API e do worker (a senha do SMTP fica cifrada no banco).</summary>
        public static IEncryptor Encryptor()
        {
            Dictionary<string, string> dicConfig = EnvFile.Read(OpsPaths.AppConfig);
            return new AesGcmEncryptor(dicConfig.GetValueOrDefault(KeyEncryption));
        }
    }
}
