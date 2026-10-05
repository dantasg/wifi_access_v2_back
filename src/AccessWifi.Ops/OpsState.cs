using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccessWifi.Ops
{
    /// <summary>
    /// O que a conferência lembra entre uma rodada e outra (/var/lib/accesswifi-ops/estado.json). Os nomes das
    /// chaves são os da primeira versão (Python), então o arquivo que já está no servidor continua valendo.
    /// </summary>
    public class OpsState
    {
        private static readonly JsonSerializerOptions s_objJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>Quando a conferência rodou pela primeira vez (referência do "backup atrasado").</summary>
        [JsonPropertyName("instalado_em")]
        public DateTimeOffset? InstalledAt { get; set; }

        [JsonPropertyName("backup_ok_em")]
        public DateTimeOffset? BackupOkAt { get; set; }

        [JsonPropertyName("backup_falhou")]
        public DateTimeOffset? BackupFailedAt { get; set; }

        /// <summary>Por peça conferida ("api", "portal:host"…): está falhando, desde quando, último aviso.</summary>
        [JsonPropertyName("checagens")]
        public Dictionary<string, CheckState> Checks { get; set; } = new Dictionary<string, CheckState>();

        public static OpsState Parse(string sJson)
        {
            try
            {
                return JsonSerializer.Deserialize<OpsState>(sJson, s_objJsonOptions) ?? new OpsState();
            }
            catch (JsonException)
            {
                return new OpsState();
            }
        }

        public static OpsState Load(string sPath = OpsPaths.State)
        {
            return File.Exists(sPath) ? Parse(File.ReadAllText(sPath)) : new OpsState();
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, s_objJsonOptions);
        }

        public void Save(string sPath = OpsPaths.State)
        {
            string? sFolder = Path.GetDirectoryName(sPath);
            if (!string.IsNullOrEmpty(sFolder))
            {
                Directory.CreateDirectory(sFolder);
            }
            string sTemp = sPath + ".tmp";
            File.WriteAllText(sTemp, ToJson());
            File.Move(sTemp, sPath, overwrite: true);
        }

        /// <summary>
        /// Trava o estado enquanto alguém lê-altera-grava: o backup (03:15) e a conferência (5 em 5 min) podem
        /// rodar juntos, e sem a trava um apagaria o que o outro acabou de gravar.
        /// </summary>
        public static async Task<IDisposable> LockAsync(string sPath = OpsPaths.State)
        {
            string sLock = sPath + ".lock";
            string? sFolder = Path.GetDirectoryName(sLock);
            if (!string.IsNullOrEmpty(sFolder))
            {
                Directory.CreateDirectory(sFolder);
            }
            DateTime dtLimit = DateTime.UtcNow.AddMinutes(3);
            while (true)
            {
                try
                {
                    return new FileStream(sLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException) when (DateTime.UtcNow < dtLimit)
                {
                    await Task.Delay(500);
                }
            }
        }
    }

    public class CheckState
    {
        [JsonPropertyName("falhando")]
        public bool Failing { get; set; }

        [JsonPropertyName("desde")]
        public DateTimeOffset? Since { get; set; }

        [JsonPropertyName("ultimo_aviso")]
        public DateTimeOffset? LastAlert { get; set; }
    }
}
