using System.Globalization;
using System.Text;
using Models.DataBase;

namespace Models.Campaigns
{
    /// <summary>
    /// Assunto e texto do e-mail de campanha que vai para a unidade, com o PDF anexo. O mesmo texto serve ao
    /// serviço (disparo e reenvio) e à demonstração do painel, que sai igual a uma campanha de verdade.
    /// </summary>
    public static class CampaignEmailText
    {
        public static string Subject(CampaignPdfData objData) =>
            $"Campanha {objData.CampaignName} — {objData.UnitName} — " +
            $"{objData.LocalDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} ({CustomersText(objData.Rows.Count)})";

        public static string Body(CampaignPdfData objData)
        {
            StringBuilder objBody = new StringBuilder();
            objBody.Append($"Olá, equipe da unidade {objData.UnitName}!\r\n\r\n");
            objBody.Append($"Segue em anexo o PDF da campanha \"{objData.CampaignName}\" com {CustomersText(objData.Rows.Count)} ");
            objBody.Append($"para vocês entrarem em contato hoje ({CampaignPdf.LongDate(objData.LocalDate)}).\r\n");
            if (objData.Kind == CampaignKind.Birthday)
            {
                (DateOnly dtStart, DateOnly dtEnd) = CampaignCalendar.BirthdayRange(objData.LocalDate);
                objBody.Append($"São os aniversariantes de {CampaignPdf.ShortDate(dtStart)} a {CampaignPdf.ShortDate(dtEnd)}.\r\n");
            }
            objBody.Append("\r\nMensagem para enviar:\r\n");
            objBody.Append(CampaignMessage.RenderShared(objData.MessageTemplate, objData.CompanyName, objData.UnitName)
                .Replace("\r\n", "\n").Replace("\n", "\r\n"));
            objBody.Append("\r\n\r\nNo PDF, clique no WhatsApp de cada cliente: a conversa abre com a mensagem pronta, ");
            objBody.Append("já com o nome dele. É só conferir e enviar.\r\n\r\n");
            objBody.Append("Mensagem automática do AccessWifi.");
            return objBody.ToString();
        }

        public static string CustomersText(int iTotal) => iTotal == 1 ? "1 cliente" : $"{iTotal} clientes";
    }
}
