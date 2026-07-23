using System.Globalization;
using System.Text;
using Models.DataBase;

namespace AccessWifiService
{
    /// <summary>Uma linha do CSV: o lead e o nome da unidade a que ele pertence.</summary>
    public readonly record struct LeadReportRow(Lead Lead, string UnitName);

    /// <summary>Gera o CSV de cadastros anexado ao relatório (UTF-8 com BOM, para o Excel).</summary>
    public static class LeadsCsv
    {
        private static readonly string[] s_arrHeader =
            ["timestamp", "unidade", "nome", "instagram", "telefone", "nascimento", "mac", "ap", "ssid"];

        public static byte[] Build(IEnumerable<LeadReportRow> objRows)
        {
            StringBuilder objBuilder = new StringBuilder();
            objBuilder.AppendLine(string.Join(",", s_arrHeader));

            foreach (LeadReportRow objRow in objRows)
            {
                Lead objLead = objRow.Lead;
                string[] arrFields =
                [
                    objLead.Timestamp.ToString("o", CultureInfo.InvariantCulture),
                    objRow.UnitName,
                    objLead.Nome,
                    objLead.Instagram,
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
