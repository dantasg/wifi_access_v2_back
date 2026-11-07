using System.Globalization;
using System.Text;
using Models.DataBase;

namespace Models.Campaigns
{
    /// <summary>
    /// Assunto e texto do e-mail de campanha que vai para a unidade, com o PDF anexo. O mesmo texto serve ao
    /// serviço (disparo e reenvio) e à demonstração do painel, que sai igual a uma campanha de verdade. Com o
    /// WhatsApp automático ligado, diz quantos já receberam e quantos o gestor precisa procurar
    /// (PROPOSTA_WHATSAPP.md, §7).
    /// </summary>
    public static class CampaignEmailText
    {
        public static string Subject(CampaignPdfData objData) =>
            $"Campanha {objData.CampaignName} — {objData.UnitName} — " +
            $"{objData.LocalDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} ({CustomersText(objData.Rows.Count)}" +
            (!objData.WhatsApp ? ")"
                : objData.ReceivedRows.Count == 0 ? ": nenhum pelo WhatsApp)"
                : $": {objData.ReceivedRows.Count} pelo WhatsApp)");

        public static string Body(CampaignPdfData objData)
        {
            StringBuilder objBody = new StringBuilder();
            objBody.Append($"Olá, equipe da unidade {objData.UnitName}!\r\n\r\n");
            int iNotReceived = objData.NotReceivedRows.Count;
            if (objData.WhatsApp)
            {
                int iReceived = objData.ReceivedRows.Count;
                objBody.Append($"A campanha \"{objData.CampaignName}\" de hoje ({CampaignPdf.LongDate(objData.LocalDate)}) foi para ");
                objBody.Append($"{CustomersText(objData.Rows.Count)}.\r\n");
                objBody.Append(iReceived switch
                {
                    0 => "Nenhum cliente recebeu a mensagem pelo WhatsApp.\r\n",
                    1 => "1 cliente já recebeu a mensagem pelo WhatsApp.\r\n",
                    _ => $"{iReceived} clientes já receberam a mensagem pelo WhatsApp.\r\n",
                });
                objBody.Append(iNotReceived == 0
                    ? "Todos receberam: o PDF em anexo é só para conferência.\r\n"
                    : iNotReceived == 1
                        ? "1 cliente não recebeu e está no PDF em anexo, com o motivo, para vocês entrarem em contato.\r\n"
                        : $"{iNotReceived} clientes não receberam e estão no PDF em anexo, com o motivo, para vocês entrarem em contato.\r\n");
            }
            else
            {
                objBody.Append($"Segue em anexo o PDF da campanha \"{objData.CampaignName}\" com {CustomersText(objData.Rows.Count)} ");
                objBody.Append($"para vocês entrarem em contato hoje ({CampaignPdf.LongDate(objData.LocalDate)}).\r\n");
            }
            if (objData.Kind == CampaignKind.Birthday)
            {
                (DateOnly dtStart, DateOnly dtEnd) = CampaignCalendar.BirthdayRange(objData.LocalDate);
                objBody.Append($"São os aniversariantes de {CampaignPdf.ShortDate(dtStart)} a {CampaignPdf.ShortDate(dtEnd)}.\r\n");
            }
            if (!objData.WhatsApp || iNotReceived > 0)
            {
                objBody.Append(objData.WhatsApp ? "\r\nMensagem para quem não recebeu:\r\n" : "\r\nMensagem para enviar:\r\n");
                objBody.Append(CampaignMessage.RenderShared(objData.MessageTemplate, objData.CompanyName, objData.UnitName)
                    .Replace("\r\n", "\n").Replace("\n", "\r\n"));
                objBody.Append("\r\n\r\nNo PDF, clique no WhatsApp de cada cliente: a conversa abre com a mensagem pronta, ");
                objBody.Append("já com o nome dele. É só conferir e enviar.\r\n\r\n");
            }
            else
            {
                objBody.Append("\r\n");
            }
            objBody.Append("Mensagem automática do AccessWifi.");
            return objBody.ToString();
        }

        public static string CustomersText(int iTotal) => iTotal == 1 ? "1 cliente" : $"{iTotal} clientes";
    }
}
