using System.Globalization;
using System.Text;
using Models.DataBase;

namespace Models.Reports
{
    /// <summary>Uma linha do CSV: o lead e o nome da unidade a que ele pertence.</summary>
    public readonly record struct LeadReportRow(Lead Lead, string UnitName);

    /// <summary>Gera o CSV de cadastros anexado ao relatório (UTF-8 com BOM, para o Excel).</summary>
    public static class LeadsCsv
    {
        private static readonly string[] s_arrHeader =
            ["cadastro", "unidade", "nome", "instagram", "telefone", "nascimento", "mac", "ap", "ssid"];

        public static byte[] Build(IEnumerable<LeadReportRow> objRows)
        {
            StringBuilder objBuilder = new StringBuilder();
            objBuilder.AppendLine(string.Join(",", s_arrHeader));

            foreach (LeadReportRow objRow in objRows)
            {
                Lead objLead = objRow.Lead;
                string[] arrFields =
                [
                    objLead.CreatedAt.ToString("o", CultureInfo.InvariantCulture),
                    objRow.UnitName,
                    objLead.Nome,
                    InstagramLink(objLead.Instagram),
                    objLead.Telefone,
                    objLead.Nascimento,
                    objLead.Mac ?? "",
                    objLead.Ap ?? "",
                    objLead.Ssid ?? "",
                ];
                objBuilder.AppendLine(string.Join(",", arrFields.Select(Escape)));
            }

            // BOM para o Excel abrir os acentos corretamente.
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(objBuilder.ToString());
        }

        /// <summary>
        /// O link do perfil, para abrir com um clique. Os cadastros novos já vêm com ele; os antigos (só o @)
        /// ganham o link aqui se estiverem no formato. Fora disso, sai como a pessoa digitou.
        /// </summary>
        private static string InstagramLink(string sInstagram)
        {
            string sUrl = InstagramHandle.ProfileUrl(sInstagram);
            return sUrl.Length > 0 ? sUrl : sInstagram;
        }

        private static string Escape(string sField)
        {
            if (sField.Contains('"') || sField.Contains(',') || sField.Contains('\n') || sField.Contains('\r'))
            {
                return "\"" + sField.Replace("\"", "\"\"") + "\"";
            }
            return sField;
        }
    }
}
