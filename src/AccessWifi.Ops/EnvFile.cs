using System.Text;

namespace AccessWifi.Ops
{
    /// <summary>
    /// Arquivo no formato CHAVE=VALOR, uma por linha (o mesmo do systemd EnvironmentFile). O valor é tudo
    /// depois do primeiro "=" — senhas podem ter "=". Linhas vazias e começadas por "#" são ignoradas.
    /// </summary>
    public static class EnvFile
    {
        public static Dictionary<string, string> Parse(string sText)
        {
            Dictionary<string, string> dicValues = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string sLine in sText.Split('\n'))
            {
                string sClean = sLine.TrimEnd('\r');
                if (sClean.Trim().Length == 0 || sClean.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                int iEquals = sClean.IndexOf('=');
                if (iEquals <= 0)
                {
                    continue;
                }
                dicValues[sClean[..iEquals].Trim()] = sClean[(iEquals + 1)..];
            }
            return dicValues;
        }

        public static Dictionary<string, string> Read(string sPath)
        {
            return File.Exists(sPath)
                ? Parse(File.ReadAllText(sPath, Encoding.UTF8))
                : new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public static string Format(IReadOnlyDictionary<string, string> dicValues, string sHeader)
        {
            StringBuilder objBuilder = new StringBuilder();
            objBuilder.Append("# ").Append(sHeader).Append('\n');
            foreach (KeyValuePair<string, string> objPair in dicValues)
            {
                if (objPair.Value.Length == 0)
                {
                    continue;
                }
                if (objPair.Value.Contains('\n') || objPair.Value.Contains('\r'))
                {
                    throw new ArgumentException($"O valor de {objPair.Key} não pode ter quebra de linha.");
                }
                objBuilder.Append(objPair.Key).Append('=').Append(objPair.Value).Append('\n');
            }
            return objBuilder.ToString();
        }

        /// <summary>Grava só para o dono (600), trocando o arquivo de uma vez (sem estado pela metade).</summary>
        public static void WriteSecret(string sPath, IReadOnlyDictionary<string, string> dicValues, string sHeader)
        {
            string? sFolder = Path.GetDirectoryName(sPath);
            if (!string.IsNullOrEmpty(sFolder))
            {
                Directory.CreateDirectory(sFolder);
            }

            string sTemp = sPath + ".tmp";
            File.WriteAllText(sTemp, Format(dicValues, sHeader), new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(sTemp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            File.Move(sTemp, sPath, overwrite: true);
        }
    }
}
